using SubscriptionManagement.Contracts;
using SubscriptionManagement.Payments.Features.Payments;

namespace SubscriptionManagement.Payments.Tests;

/// <summary>
/// The handlers on a real PostgreSQL through the real Wolverine pipeline. No handler calls SaveChanges: a stream that
/// is there afterwards was committed by Wolverine's Marten transaction, the one that also stores the outgoing messages.
/// Amounts follow the fixture's gateway thresholds: below 100 captures, from 100 declines, from 200.50 never answers.
/// </summary>
[Collection(PaymentsCollection.Name)]
public class PaymentHandlerTests(
  PaymentsFixture fixture
)
{
  private static readonly DateOnly PeriodEnd = new(2027, 1, 15);

  private static ChargePayment Charge(
    decimal amount,
    Guid? subscriptionId = null,
    DateOnly? periodEnd = null
  )
  {
    return new ChargePayment(
      Guid.NewGuid(),
      Guid.NewGuid(),
      subscriptionId ?? Guid.NewGuid(),
      amount,
      "USD",
      periodEnd ?? PeriodEnd
    );
  }

  [Fact]
  public async Task Charge_appends_request_and_capture_and_publishes_success()
  {
    var charge = Charge(11m);

    var tracked = await fixture.DeliverAsync(charge);

    Assert.Equal(
      [
        new ChargeRequested(charge.RenewalSagaId, charge.SubscriptionId, 11m, "USD", PeriodEnd),
        new ChargeCaptured()
      ],
      await fixture.StreamAsync(charge.PaymentId)
    );
    Assert.Equal(
      new PaymentSucceeded(charge.RenewalSagaId, charge.PaymentId),
      Assert.Single(tracked.Sent.AllMessages())
    );

    var snapshot = await fixture.SnapshotAsync(charge.PaymentId);
    Assert.NotNull(snapshot);
    Assert.Equal(charge.PaymentId, snapshot.Id);
    Assert.Equal(charge.RenewalSagaId, snapshot.InvoiceId);
    Assert.Equal(charge.SubscriptionId, snapshot.SubscriptionId);
    Assert.Equal(11m, snapshot.Amount);
    Assert.Equal("USD", snapshot.Currency);
    Assert.Equal(PeriodEnd, snapshot.PeriodEnd);
    Assert.Equal(PaymentStatus.Captured, snapshot.Status);
    Assert.Equal(2, snapshot.Version);
  }

  [Fact]
  public async Task Declined_charge_appends_the_decline_and_publishes_failure()
  {
    var charge = Charge(150m);

    var tracked = await fixture.DeliverAsync(charge);

    Assert.Equal(
      [
        new ChargeRequested(charge.RenewalSagaId, charge.SubscriptionId, 150m, "USD", PeriodEnd),
        new ChargeDeclined("card-declined")
      ],
      await fixture.StreamAsync(charge.PaymentId)
    );
    Assert.Equal(
      new PaymentFailed(charge.RenewalSagaId, charge.PaymentId, "card-declined"),
      Assert.Single(tracked.Sent.AllMessages())
    );
    Assert.Equal(PaymentStatus.Declined, (await fixture.SnapshotAsync(charge.PaymentId))!.Status);
  }

  [Fact]
  public async Task Unanswered_charge_is_recorded_and_publishes_nothing()
  {
    var charge = Charge(200.50m);

    var tracked = await fixture.DeliverAsync(charge);

    Assert.Equal(
      [new ChargeRequested(charge.RenewalSagaId, charge.SubscriptionId, 200.50m, "USD", PeriodEnd)],
      await fixture.StreamAsync(charge.PaymentId)
    );
    Assert.Empty(tracked.Sent.AllMessages());
    Assert.Equal(PaymentStatus.Requested, (await fixture.SnapshotAsync(charge.PaymentId))!.Status);
  }

  [Fact]
  public async Task Redelivered_charge_is_a_no_op()
  {
    var charge = Charge(11m);
    await fixture.DeliverAsync(charge);
    var before = await fixture.StreamAsync(charge.PaymentId);

    var tracked = await fixture.DeliverAsync(charge);

    Assert.Equal(before, await fixture.StreamAsync(charge.PaymentId));
    Assert.Empty(tracked.Sent.AllMessages());
    Assert.Equal(2, (await fixture.SnapshotAsync(charge.PaymentId))!.Version);
  }

  [Fact]
  public async Task Cancel_before_charge_starts_the_stream_and_blocks_the_late_charge()
  {
    var charge = Charge(11m);

    var canceled = await fixture.DeliverAsync(new CancelPayment(charge.PaymentId, "timeout"));

    Assert.Equal([new ChargeCanceled("timeout")], await fixture.StreamAsync(charge.PaymentId));
    Assert.Empty(canceled.Sent.AllMessages());

    var late = await fixture.DeliverAsync(charge);

    Assert.Equal([new ChargeCanceled("timeout")], await fixture.StreamAsync(charge.PaymentId));
    Assert.Empty(late.Sent.AllMessages());
    var snapshot = await fixture.SnapshotAsync(charge.PaymentId);
    Assert.Equal(PaymentStatus.Canceled, snapshot!.Status);
    Assert.Equal(1, snapshot.Version);
  }

  [Fact]
  public async Task Cancel_of_an_unanswered_charge_cancels_it()
  {
    var charge = Charge(200.50m);
    await fixture.DeliverAsync(charge);

    var tracked = await fixture.DeliverAsync(new CancelPayment(charge.PaymentId, "timeout"));

    Assert.Equal(new ChargeCanceled("timeout"), (await fixture.StreamAsync(charge.PaymentId))[^1]);
    Assert.Empty(tracked.Sent.AllMessages());
    Assert.Equal(PaymentStatus.Canceled, (await fixture.SnapshotAsync(charge.PaymentId))!.Status);
  }

  [Fact]
  public async Task Cancel_after_capture_refunds_once_however_often_it_is_delivered()
  {
    var charge = Charge(11m);
    await fixture.DeliverAsync(charge);
    var cancel = new CancelPayment(charge.PaymentId, "settlement-rejected");

    var first = await fixture.DeliverAsync(cancel);

    // The refund message is built from the stored snapshot, not from the command.
    Assert.Equal(
      new PaymentRefunded(
        charge.PaymentId,
        charge.RenewalSagaId,
        charge.SubscriptionId,
        11m,
        "USD",
        "settlement-rejected"
      ),
      Assert.Single(first.Sent.AllMessages())
    );

    var second = await fixture.DeliverAsync(cancel);

    Assert.Empty(second.Sent.AllMessages());
    Assert.Equal(
      [
        new ChargeRequested(charge.RenewalSagaId, charge.SubscriptionId, 11m, "USD", PeriodEnd),
        new ChargeCaptured(),
        new ChargeRefunded("settlement-rejected")
      ],
      await fixture.StreamAsync(charge.PaymentId)
    );
    var snapshot = await fixture.SnapshotAsync(charge.PaymentId);
    Assert.Equal(PaymentStatus.Refunded, snapshot!.Status);
    Assert.Equal(3, snapshot.Version);
  }

  [Fact]
  public async Task Subscription_cancellation_refunds_only_captured_payments_of_that_subscription_with_time_left()
  {
    var subscriptionId = Guid.NewGuid();
    var canceledOn = new DateOnly(2026, 12, 20);
    var current = Charge(11m, subscriptionId, new DateOnly(2027, 1, 15));
    var elapsed = Charge(12m, subscriptionId, canceledOn);
    var declined = Charge(150m, subscriptionId, new DateOnly(2027, 1, 15));
    var otherSubscription = Charge(13m, periodEnd: new DateOnly(2027, 1, 15));
    foreach (var charge in new[] { current, elapsed, declined, otherSubscription })
      await fixture.DeliverAsync(charge);

    var tracked = await fixture.DeliverAsync(new SubscriptionCanceled(subscriptionId, canceledOn));

    Assert.Equal(
      new PaymentRefunded(
        current.PaymentId,
        current.RenewalSagaId,
        subscriptionId,
        11m,
        "USD",
        "subscription-canceled"
      ),
      Assert.Single(tracked.Sent.AllMessages())
    );
    Assert.Equal(new ChargeRefunded("subscription-canceled"), (await fixture.StreamAsync(current.PaymentId))[^1]);
    Assert.Equal(PaymentStatus.Refunded, (await fixture.SnapshotAsync(current.PaymentId))!.Status);
    Assert.Equal(PaymentStatus.Captured, (await fixture.SnapshotAsync(elapsed.PaymentId))!.Status);
    Assert.Equal(PaymentStatus.Declined, (await fixture.SnapshotAsync(declined.PaymentId))!.Status);
    Assert.Equal(PaymentStatus.Captured, (await fixture.SnapshotAsync(otherSubscription.PaymentId))!.Status);

    // The fact delivered again finds nothing left to refund.
    var again = await fixture.DeliverAsync(new SubscriptionCanceled(subscriptionId, canceledOn));

    Assert.Empty(again.Sent.AllMessages());
    Assert.Equal(3, (await fixture.StreamAsync(current.PaymentId)).Count);
  }

  [Fact]
  public async Task Subscription_cancellation_refunds_every_open_period_in_one_go()
  {
    var subscriptionId = Guid.NewGuid();
    var first = Charge(11m, subscriptionId, new DateOnly(2027, 1, 15));
    var second = Charge(11m, subscriptionId, new DateOnly(2027, 2, 15));
    await fixture.DeliverAsync(first);
    await fixture.DeliverAsync(second);

    var tracked = await fixture.DeliverAsync(new SubscriptionCanceled(subscriptionId, new DateOnly(2026, 12, 20)));

    Assert.Equal(
      new[] { first.PaymentId, second.PaymentId }.Order(),
      tracked.Sent.MessagesOf<PaymentRefunded>().Select(m => m.PaymentId).Order()
    );
    Assert.Equal(2, tracked.Sent.AllMessages().Count());
  }

  [Fact]
  public async Task Subscription_cancellation_without_payments_ends_the_flow()
  {
    var tracked = await fixture.DeliverAsync(new SubscriptionCanceled(Guid.NewGuid(), new DateOnly(2026, 12, 20)));

    Assert.Empty(tracked.Sent.AllMessages());
  }

  [Fact]
  public async Task Charges_racing_for_one_payment_capture_it_once()
  {
    var charge = Charge(11m);

    var tracked = await fixture.DeliverConcurrentlyAsync([.. Enumerable.Repeat<object>(charge, 8)]);

    Assert.Equal(
      [
        new ChargeRequested(charge.RenewalSagaId, charge.SubscriptionId, 11m, "USD", PeriodEnd),
        new ChargeCaptured()
      ],
      await fixture.StreamAsync(charge.PaymentId)
    );
    Assert.Equal(
      new PaymentSucceeded(charge.RenewalSagaId, charge.PaymentId),
      Assert.Single(tracked.Sent.AllMessages())
    );
  }

  [Fact]
  public async Task Cancel_racing_its_charge_never_leaves_the_money_captured()
  {
    var charge = Charge(11m);

    var tracked = await fixture.DeliverConcurrentlyAsync(charge, new CancelPayment(charge.PaymentId, "timeout"));

    // Either order is legal; what must not happen is both starting the stream, or a capture that stays.
    var stream = await fixture.StreamAsync(charge.PaymentId);
    if (stream[0] is ChargeCanceled)
    {
      Assert.Equal([new ChargeCanceled("timeout")], stream);
      Assert.Empty(tracked.Sent.AllMessages());
    }
    else
    {
      Assert.Equal(
        [
          new ChargeRequested(charge.RenewalSagaId, charge.SubscriptionId, 11m, "USD", PeriodEnd),
          new ChargeCaptured(),
          new ChargeRefunded("timeout")
        ],
        stream
      );
      Assert.Equal(
        new PaymentSucceeded(charge.RenewalSagaId, charge.PaymentId),
        tracked.Sent.SingleMessage<PaymentSucceeded>()
      );
      Assert.Equal(
        new PaymentRefunded(
          charge.PaymentId,
          charge.RenewalSagaId,
          charge.SubscriptionId,
          11m,
          "USD",
          "timeout"
        ),
        tracked.Sent.SingleMessage<PaymentRefunded>()
      );
    }
  }
}