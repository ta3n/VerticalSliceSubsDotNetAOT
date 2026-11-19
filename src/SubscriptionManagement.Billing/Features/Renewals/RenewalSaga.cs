using SubscriptionManagement.Billing.Infrastructure;
using SubscriptionManagement.Contracts;
using Wolverine;

namespace SubscriptionManagement.Billing.Features.Renewals;

/// <summary>Stored by name in <c>renewal_sagas.state</c> and returned as-is by <c>GetRenewal</c>.</summary>
public enum RenewalState
{
    AwaitingPayment,
    Settling,
    Declined,
    TimedOut,
    Completed,
    Compensated
}

/// <summary>Scheduled when the saga starts. Handled by Billing itself, so it never leaves the local queue.</summary>
public sealed record RenewalTimeout(
    Guid RenewalSagaId
);

/// <summary>
/// Collects the payment of one renewal invoice: charge, settle, and undo whichever half succeeded when the other
/// fails or the payment reply does not arrive in time. The saga id is the invoice id.
/// <para>
/// The row is the process record: it is kept with a terminal state and never deleted (no <c>MarkCompleted()</c>), so a
/// late or repeated message finds it and is ignored. Every method below is one row of the transition table: it only
/// changes this object and returns the messages to send. Wolverine loads the row, persists it and sends the messages
/// in one transaction; <see cref="Saga.Version"/> is the concurrency token, so of two messages racing for the same
/// renewal one is retried against the state the other left behind.
/// </para>
/// </summary>
public sealed class RenewalSaga : Saga
{
    public const string TimeoutReason = "Payment was not confirmed in time.";

    public Guid Id { get; set; }
    public Guid SubscriptionId { get; set; }
    public Guid PaymentId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public RenewalState State { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>none + InvoiceIssued: request the charge and start the clock.</summary>
    public static (RenewalSaga, OutgoingMessages) Start(
        InvoiceIssued issued,
        BillingOptions options,
        TimeProvider time
    )
    {
        var saga = new RenewalSaga
        {
            Id = issued.InvoiceId,
            SubscriptionId = issued.SubscriptionId,
            PaymentId = Guid.NewGuid(),
            Amount = issued.Amount,
            Currency = issued.Currency,
            State = RenewalState.AwaitingPayment,
            UpdatedAt = time.GetUtcNow()
        };

        var messages = new OutgoingMessages
        {
            new ChargePayment(
                saga.Id,
                saga.PaymentId,
                saga.SubscriptionId,
                saga.Amount,
                saga.Currency,
                issued.PeriodEnd
            )
        };
        messages.Delay(new RenewalTimeout(saga.Id), options.RenewalTimeout);

        return (saga, messages);
    }

    /// <summary>AwaitingPayment + PaymentSucceeded: the money is captured, mark the invoice paid.</summary>
    public OutgoingMessages Handle(
        PaymentSucceeded message,
        TimeProvider time,
        ILogger<RenewalSaga> logger
    )
    {
        if (State != RenewalState.AwaitingPayment)
        {
            return Ignore(message, logger);
        }

        MoveTo(RenewalState.Settling, null, time);
        return [new SettleInvoice(Id, PaymentId)];
    }

    /// <summary>AwaitingPayment + PaymentFailed: nothing was captured, cancel the renewal.</summary>
    public OutgoingMessages Handle(
        PaymentFailed message,
        TimeProvider time,
        ILogger<RenewalSaga> logger
    )
    {
        if (State != RenewalState.AwaitingPayment)
        {
            return Ignore(message, logger);
        }

        MoveTo(RenewalState.Declined, message.Reason, time);
        return [new CancelRenewal(Id, SubscriptionId, message.Reason)];
    }

    /// <summary>
    /// AwaitingPayment + RenewalTimeout: no reply, so cancel both sides. Once the payment is confirmed the timeout no
    /// longer acts: canceling in Settling would refund a payment whose invoice is about to be marked paid.
    /// </summary>
    public OutgoingMessages Handle(
        RenewalTimeout message,
        TimeProvider time,
        ILogger<RenewalSaga> logger
    )
    {
        if (State != RenewalState.AwaitingPayment)
        {
            return Ignore(message, logger);
        }

        MoveTo(RenewalState.TimedOut, TimeoutReason, time);
        return [new CancelPayment(PaymentId, TimeoutReason), new CancelRenewal(Id, SubscriptionId, TimeoutReason)];
    }

    /// <summary>Settling + InvoiceSettled: done.</summary>
    public OutgoingMessages Handle(
        InvoiceSettled message,
        TimeProvider time,
        ILogger<RenewalSaga> logger
    )
    {
        if (State != RenewalState.Settling)
        {
            return Ignore(message, logger);
        }

        MoveTo(RenewalState.Completed, null, time);
        return [];
    }

    /// <summary>Settling + InvoiceSettlementRejected: the invoice cannot take the payment, give the money back.</summary>
    public OutgoingMessages Handle(
        InvoiceSettlementRejected message,
        TimeProvider time,
        ILogger<RenewalSaga> logger
    )
    {
        if (State != RenewalState.Settling)
        {
            return Ignore(message, logger);
        }

        MoveTo(RenewalState.Compensated, message.Reason, time);
        return [new CancelPayment(PaymentId, message.Reason)];
    }

    // A reply or timeout for an invoice that never started a saga. Without these Wolverine fails the message.
    public static void NotFound(
        PaymentSucceeded message
    )
    {
    }

    public static void NotFound(
        PaymentFailed message
    )
    {
    }

    public static void NotFound(
        RenewalTimeout message
    )
    {
    }

    public static void NotFound(
        InvoiceSettled message
    )
    {
    }

    public static void NotFound(
        InvoiceSettlementRejected message
    )
    {
    }

    private void MoveTo(
        RenewalState next,
        string? failureReason,
        TimeProvider time
    )
    {
        State = next;
        FailureReason = failureReason;
        UpdatedAt = time.GetUtcNow();
    }

    // Any other state/message pair: a duplicate, or the loser of a race. Nothing changes, so nothing is written.
    private OutgoingMessages Ignore(
        object message,
        ILogger logger
    )
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Renewal {InvoiceId} is {State}; ignoring {MessageType}.",
                Id,
                State,
                message.GetType().Name
            );
        }

        return [];
    }
}
