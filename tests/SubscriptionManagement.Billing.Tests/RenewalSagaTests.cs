using Microsoft.Extensions.Logging.Abstractions;
using SubscriptionManagement.Billing.Features.Renewals;
using SubscriptionManagement.Billing.Infrastructure;
using SubscriptionManagement.Contracts;
using Wolverine;

namespace SubscriptionManagement.Billing.Tests;

/// <summary>
/// The saga's transition table, without a host or a database: each method changes the saga object and returns the
/// messages to send.
/// </summary>
public class RenewalSagaTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = StartedAt.AddMinutes(1);
    private static readonly BillingOptions Options = new(TimeSpan.FromSeconds(30), 0.10m);

    private static readonly InvoiceIssued Issued = new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        11.0000m,
        "USD",
        new DateOnly(2026, 9, 12),
        new DateOnly(2026, 10, 12)
    );

    [Fact]
    public void InvoiceIssued_starts_the_saga_requests_the_charge_and_schedules_the_timeout()
    {
        var (saga, messages) = RenewalSaga.Start(Issued, Options, new FixedTime(StartedAt));

        Assert.Equal(Issued.InvoiceId, saga.Id);
        Assert.Equal(Issued.SubscriptionId, saga.SubscriptionId);
        Assert.Equal(11.0000m, saga.Amount);
        Assert.Equal("USD", saga.Currency);
        Assert.Equal(RenewalState.AwaitingPayment, saga.State);
        Assert.Null(saga.FailureReason);
        Assert.Equal(StartedAt, saga.UpdatedAt);
        Assert.NotEqual(Guid.Empty, saga.PaymentId);

        Assert.Equal(2, messages.Count);
        Assert.Equal(
            new ChargePayment(saga.Id, saga.PaymentId, saga.SubscriptionId, 11.0000m, "USD", Issued.PeriodEnd),
            Assert.Single(messages.OfType<ChargePayment>())
        );
        var timeout = Assert.Single(messages.OfType<DeliveryMessage<RenewalTimeout>>());
        Assert.Equal(new RenewalTimeout(saga.Id), timeout.Message);
        Assert.Equal(Options.RenewalTimeout, timeout.Options.ScheduleDelay);
    }

    [Fact]
    public void Each_renewal_gets_its_own_payment_id()
    {
        var (first, _) = RenewalSaga.Start(Issued, Options, new FixedTime(StartedAt));
        var (second, _) = RenewalSaga.Start(Issued, Options, new FixedTime(StartedAt));

        Assert.NotEqual(first.PaymentId, second.PaymentId);
    }

    [Fact]
    public void AwaitingPayment_PaymentSucceeded_settles_the_invoice()
    {
        var saga = SagaIn(RenewalState.AwaitingPayment);

        var messages = Deliver(saga, new PaymentSucceeded(saga.Id, saga.PaymentId));

        Assert.Equal([new SettleInvoice(saga.Id, saga.PaymentId)], messages);
        AssertMovedTo(saga, RenewalState.Settling, null);
    }

    [Fact]
    public void AwaitingPayment_PaymentFailed_cancels_the_renewal_and_is_terminal()
    {
        var saga = SagaIn(RenewalState.AwaitingPayment);

        var messages = Deliver(saga, new PaymentFailed(saga.Id, saga.PaymentId, "card declined"));

        Assert.Equal([new CancelRenewal(saga.Id, saga.SubscriptionId, "card declined")], messages);
        AssertMovedTo(saga, RenewalState.Declined, "card declined");
    }

    [Fact]
    public void AwaitingPayment_RenewalTimeout_cancels_the_payment_and_the_renewal_and_is_terminal()
    {
        var saga = SagaIn(RenewalState.AwaitingPayment);

        var messages = Deliver(saga, new RenewalTimeout(saga.Id));

        Assert.Equal(
            [
                new CancelPayment(saga.PaymentId, RenewalSaga.TimeoutReason),
                new CancelRenewal(saga.Id, saga.SubscriptionId, RenewalSaga.TimeoutReason)
            ],
            messages
        );
        AssertMovedTo(saga, RenewalState.TimedOut, RenewalSaga.TimeoutReason);
    }

    [Fact]
    public void Settling_InvoiceSettled_completes_without_sending_anything()
    {
        var saga = SagaIn(RenewalState.Settling);

        var messages = Deliver(saga, new InvoiceSettled(saga.Id));

        Assert.Empty(messages);
        AssertMovedTo(saga, RenewalState.Completed, null);
    }

    [Fact]
    public void Settling_InvoiceSettlementRejected_refunds_the_payment_and_is_terminal()
    {
        var saga = SagaIn(RenewalState.Settling);

        var messages = Deliver(saga, new InvoiceSettlementRejected(saga.Id, "invoice-not-payable"));

        Assert.Equal([new CancelPayment(saga.PaymentId, "invoice-not-payable")], messages);
        AssertMovedTo(saga, RenewalState.Compensated, "invoice-not-payable");
    }

    /// <summary>Every state/message pair that is not one of the five transitions above: 6 states x 5 messages - 5.</summary>
    public static TheoryData<RenewalState, string> IgnoredPairs()
    {
        (RenewalState, string)[] acting =
        [
            (RenewalState.AwaitingPayment, nameof(PaymentSucceeded)),
            (RenewalState.AwaitingPayment, nameof(PaymentFailed)),
            (RenewalState.AwaitingPayment, nameof(RenewalTimeout)),
            (RenewalState.Settling, nameof(InvoiceSettled)),
            (RenewalState.Settling, nameof(InvoiceSettlementRejected))
        ];

        var data = new TheoryData<RenewalState, string>();
        foreach (var state in Enum.GetValues<RenewalState>())
        foreach (var message in MessageNames)
            if (!acting.Contains((state, message)))
            {
                data.Add(state, message);
            }

        return data;
    }

    [Theory]
    [MemberData(nameof(IgnoredPairs))]
    public void Any_other_pair_sends_nothing_and_leaves_the_saga_unchanged(
        RenewalState state,
        string message
    )
    {
        var saga = SagaIn(state);
        saga.FailureReason = "earlier reason";

        var messages = Deliver(saga, MessageNamed(message, saga));

        Assert.Empty(messages);
        Assert.Equal(state, saga.State);
        Assert.Equal("earlier reason", saga.FailureReason);
        Assert.Equal(StartedAt, saga.UpdatedAt);
        Assert.False(saga.IsCompleted());
    }

    [Fact]
    public void The_ignored_pairs_cover_the_rest_of_the_table()
    {
        Assert.Equal(6 * 5 - 5, IgnoredPairs().Count);
    }

    private static readonly string[] MessageNames =
    [
        nameof(PaymentSucceeded), nameof(PaymentFailed), nameof(RenewalTimeout), nameof(InvoiceSettled),
        nameof(InvoiceSettlementRejected)
    ];

    private static object MessageNamed(
        string name,
        RenewalSaga saga
    )
    {
        return name switch
        {
            nameof(PaymentSucceeded) => new PaymentSucceeded(saga.Id, saga.PaymentId),
            nameof(PaymentFailed) => new PaymentFailed(saga.Id, saga.PaymentId, "late decline"),
            nameof(RenewalTimeout) => new RenewalTimeout(saga.Id),
            nameof(InvoiceSettled) => new InvoiceSettled(saga.Id),
            nameof(InvoiceSettlementRejected) => new InvoiceSettlementRejected(saga.Id, "late rejection"),
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
    }

    private static RenewalSaga SagaIn(
        RenewalState state
    )
    {
        var (saga, _) = RenewalSaga.Start(Issued, Options, new FixedTime(StartedAt));
        saga.State = state;
        return saga;
    }

    private static OutgoingMessages Deliver(
        RenewalSaga saga,
        object message
    )
    {
        var time = new FixedTime(Later);
        var logger = NullLogger<RenewalSaga>.Instance;
        return message switch
        {
            PaymentSucceeded m => saga.Handle(m, time, logger),
            PaymentFailed m => saga.Handle(m, time, logger),
            RenewalTimeout m => saga.Handle(m, time, logger),
            InvoiceSettled m => saga.Handle(m, time, logger),
            InvoiceSettlementRejected m => saga.Handle(m, time, logger),
            _ => throw new ArgumentOutOfRangeException(nameof(message))
        };
    }

    private static void AssertMovedTo(
        RenewalSaga saga,
        RenewalState state,
        string? failureReason
    )
    {
        Assert.Equal(state, saga.State);
        Assert.Equal(failureReason, saga.FailureReason);
        Assert.Equal(Later, saga.UpdatedAt);
        // The row is the process record: Wolverine deletes a saga that reports itself completed.
        Assert.False(saga.IsCompleted());
    }

    private sealed class FixedTime(
        DateTimeOffset now
    ) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }
}