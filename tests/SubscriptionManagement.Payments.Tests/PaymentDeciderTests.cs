using SubscriptionManagement.Contracts;
using SubscriptionManagement.Payments.Features.Payments;
using SubscriptionManagement.Payments.Infrastructure;

namespace SubscriptionManagement.Payments.Tests;

/// <summary>
/// The payment rules without a database: given the events already on a stream, a message yields new events and
/// outgoing messages. Records compare by value, so whole event and message lists are asserted.
/// </summary>
public class PaymentDeciderTests
{
  private static readonly GatewayOptions Gateway = new() { DeclineAtOrAbove = 5000m, NoReplyAtOrAbove = 9000m };

  private static readonly Guid PaymentId = Guid.NewGuid();
  private static readonly Guid InvoiceId = Guid.NewGuid();
  private static readonly Guid SubscriptionId = Guid.NewGuid();
  private static readonly DateOnly PeriodEnd = new(2027, 1, 15);

  private static readonly ChargeRequested Requested = new(InvoiceId, SubscriptionId, 11m, "USD", PeriodEnd);

  public static TheoryData<Type[]> ExistingStreams => new()
  {
    new[] { typeof(ChargeRequested) },
    new[] { typeof(ChargeRequested), typeof(ChargeCaptured) },
    new[] { typeof(ChargeRequested), typeof(ChargeDeclined) },
    new[] { typeof(ChargeRequested), typeof(ChargeCanceled) },
    new[] { typeof(ChargeRequested), typeof(ChargeCaptured), typeof(ChargeRefunded) },
    // The cancel overtook the charge: the late charge must not capture.
    new[] { typeof(ChargeCanceled) }
  };

  public static TheoryData<Type[]> SettledStreams => new()
  {
    // Double refund.
    new[] { typeof(ChargeRequested), typeof(ChargeCaptured), typeof(ChargeRefunded) },
    new[] { typeof(ChargeRequested), typeof(ChargeDeclined) },
    new[] { typeof(ChargeRequested), typeof(ChargeCanceled) },
    new[] { typeof(ChargeCanceled) }
  };

  public static TheoryData<Type[]> UncapturedStreams => new()
  {
    new[] { typeof(ChargeRequested) },
    new[] { typeof(ChargeRequested), typeof(ChargeDeclined) },
    new[] { typeof(ChargeRequested), typeof(ChargeCanceled) },
    // Already refunded: a redelivered cancellation must not refund twice.
    new[] { typeof(ChargeRequested), typeof(ChargeCaptured), typeof(ChargeRefunded) }
  };

  private static ChargePayment Charge(
    decimal amount
  )
  {
    return new ChargePayment(InvoiceId, PaymentId, SubscriptionId, amount, "USD", PeriodEnd);
  }

  /// <summary>Folds events the way the inline snapshot does; no events means the stream does not exist.</summary>
  private static Payment? Given(
    params object[] events
  )
  {
    if (events.Length == 0) return null;

    var payment = new Payment { Id = PaymentId };
    foreach (var e in events)
    {
      switch (e)
      {
        case ChargeRequested x: payment.Apply(x); break;
        case ChargeCaptured x: payment.Apply(x); break;
        case ChargeDeclined x: payment.Apply(x); break;
        case ChargeCanceled x: payment.Apply(x); break;
        case ChargeRefunded x: payment.Apply(x); break;
        default: throw new ArgumentException($"Unknown event {e.GetType().Name}.");
      }

      payment.Version++;
    }

    return payment;
  }

  private static Payment? GivenHistory(
    Type[] eventTypes
  )
  {
    return Given(
      eventTypes.Select(
          type => type.Name switch
          {
            nameof(ChargeRequested) => (object)Requested,
            nameof(ChargeCaptured) => new ChargeCaptured(),
            nameof(ChargeDeclined) => new ChargeDeclined("card-declined"),
            nameof(ChargeCanceled) => new ChargeCanceled("timeout"),
            nameof(ChargeRefunded) => new ChargeRefunded("already-refunded"),
            _ => throw new ArgumentException($"Unknown event type {type.Name}.")
          }
        )
        .ToArray()
    );
  }

  [Fact]
  public void Charge_below_the_decline_threshold_is_captured_and_reported_to_the_saga()
  {
    var decision = PaymentDecider.Decide(Given(), Charge(11m), Gateway);

    Assert.Equal([Requested, new ChargeCaptured()], decision.Events);
    Assert.Equal([new PaymentSucceeded(InvoiceId, PaymentId)], decision.Messages);
  }

  [Fact]
  public void Charge_at_or_above_the_decline_threshold_is_declined_with_a_reason()
  {
    var decision = PaymentDecider.Decide(Given(), Charge(6000m), Gateway);

    Assert.Equal([Requested with { Amount = 6000m }, new ChargeDeclined("card-declined")], decision.Events);
    Assert.Equal([new PaymentFailed(InvoiceId, PaymentId, "card-declined")], decision.Messages);
  }

  [Fact]
  public void Charge_at_or_above_the_no_reply_threshold_is_recorded_but_never_answered()
  {
    var decision = PaymentDecider.Decide(Given(), Charge(9500m), Gateway);

    Assert.Equal([Requested with { Amount = 9500m }], decision.Events);
    Assert.Empty(decision.Messages);
  }

  [Theory]
  [InlineData("4999.9999", typeof(ChargeCaptured))]
  [InlineData("5000", typeof(ChargeDeclined))]
  [InlineData("8999.9999", typeof(ChargeDeclined))]
  [InlineData("9000", typeof(ChargeRequested))]
  public void Thresholds_are_inclusive_lower_bounds(
    string amount,
    Type lastEvent
  )
  {
    var decision = PaymentDecider.Decide(Given(), Charge(decimal.Parse(amount)), Gateway);

    Assert.IsType(lastEvent, decision.Events[^1]);
  }

  [Fact]
  public void Thresholds_come_from_the_options()
  {
    var strict = new GatewayOptions { DeclineAtOrAbove = 10m, NoReplyAtOrAbove = 20m };

    Assert.IsType<ChargeDeclined>(PaymentDecider.Decide(Given(), Charge(11m), strict).Events[^1]);
    Assert.IsType<ChargeRequested>(PaymentDecider.Decide(Given(), Charge(20m), strict).Events[^1]);
  }

  [Theory]
  [MemberData(nameof(ExistingStreams))]
  public void Charge_on_an_existing_stream_does_nothing(
    Type[] history
  )
  {
    var decision = PaymentDecider.Decide(GivenHistory(history), Charge(11m), Gateway);

    Assert.Empty(decision.Events);
    Assert.Empty(decision.Messages);
  }

  [Fact]
  public void Cancel_before_the_charge_arrived_starts_the_stream_as_canceled()
  {
    var decision = PaymentDecider.Decide(Given(), new CancelPayment(PaymentId, "timeout"));

    Assert.Equal([new ChargeCanceled("timeout")], decision.Events);
    Assert.Empty(decision.Messages);
  }

  [Fact]
  public void Cancel_of_an_unanswered_charge_cancels_it_without_a_refund()
  {
    var decision = PaymentDecider.Decide(Given(Requested), new CancelPayment(PaymentId, "timeout"));

    Assert.Equal([new ChargeCanceled("timeout")], decision.Events);
    Assert.Empty(decision.Messages);
  }

  [Fact]
  public void Cancel_after_capture_refunds_and_announces_it_with_the_charge_details()
  {
    var decision = PaymentDecider.Decide(
      Given(Requested, new ChargeCaptured()),
      new CancelPayment(PaymentId, "settlement-rejected")
    );

    Assert.Equal([new ChargeRefunded("settlement-rejected")], decision.Events);
    Assert.Equal(
      [new PaymentRefunded(PaymentId, InvoiceId, SubscriptionId, 11m, "USD", "settlement-rejected")],
      decision.Messages
    );
  }

  [Theory]
  [MemberData(nameof(SettledStreams))]
  public void Cancel_of_a_refunded_declined_or_canceled_payment_does_nothing(
    Type[] history
  )
  {
    var decision = PaymentDecider.Decide(GivenHistory(history), new CancelPayment(PaymentId, "again"));

    Assert.Empty(decision.Events);
    Assert.Empty(decision.Messages);
  }

  [Fact]
  public void Subscription_cancellation_refunds_a_captured_payment_whose_period_has_not_ended()
  {
    var decision = PaymentDecider.Decide(
      Given(Requested, new ChargeCaptured())!,
      new SubscriptionCanceled(SubscriptionId, PeriodEnd.AddDays(-1))
    );

    Assert.Equal([new ChargeRefunded("subscription-canceled")], decision.Events);
    Assert.Equal(
      [new PaymentRefunded(PaymentId, InvoiceId, SubscriptionId, 11m, "USD", "subscription-canceled")],
      decision.Messages
    );
  }

  [Theory]
  [InlineData(0)]
  [InlineData(1)]
  public void Subscription_cancellation_on_or_after_the_period_end_refunds_nothing(
    int daysAfterPeriodEnd
  )
  {
    var decision = PaymentDecider.Decide(
      Given(Requested, new ChargeCaptured())!,
      new SubscriptionCanceled(SubscriptionId, PeriodEnd.AddDays(daysAfterPeriodEnd))
    );

    Assert.Empty(decision.Events);
    Assert.Empty(decision.Messages);
  }

  [Theory]
  [MemberData(nameof(UncapturedStreams))]
  public void Subscription_cancellation_ignores_payments_that_hold_no_money(
    Type[] history
  )
  {
    var decision = PaymentDecider.Decide(
      GivenHistory(history)!,
      new SubscriptionCanceled(SubscriptionId, PeriodEnd.AddDays(-1))
    );

    Assert.Empty(decision.Events);
    Assert.Empty(decision.Messages);
  }
}