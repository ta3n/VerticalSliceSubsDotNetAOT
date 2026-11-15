using System.Text.Json.Serialization;

namespace SubscriptionManagement.Payments.Features.Payments;

/// <summary>Numeric values are stored in the snapshot document.</summary>
public enum PaymentStatus
{
    Requested = 1,
    Captured = 2,
    Declined = 3,
    Canceled = 4,
    Refunded = 5
}

/// <summary>
/// Current state of one payment, folded from its event stream (stream id = payment id). Marten rebuilds and stores it
/// in the transaction that appends the events, so it is never ahead of or behind the stream. Nothing else writes it:
/// the only way to change a payment is to append an event.
/// </summary>
public sealed class Payment
{
    // Id and Version are assigned by Marten (stream id and stream version).
    public Guid Id { get; set; }
    public int Version { get; set; }

    [JsonInclude]
    public Guid InvoiceId { get; private set; }

    [JsonInclude]
    public Guid SubscriptionId { get; private set; }

    [JsonInclude]
    public decimal Amount { get; private set; }

    [JsonInclude]
    public string Currency { get; private set; } = "";

    [JsonInclude]
    public DateOnly PeriodEnd { get; private set; }

    [JsonInclude]
    public PaymentStatus Status { get; private set; }

    public void Apply(
        ChargeRequested e
    )
    {
        InvoiceId = e.InvoiceId;
        SubscriptionId = e.SubscriptionId;
        Amount = e.Amount;
        Currency = e.Currency;
        PeriodEnd = e.PeriodEnd;
        Status = PaymentStatus.Requested;
    }

    public void Apply(
        ChargeCaptured e
    )
    {
        Status = PaymentStatus.Captured;
    }

    public void Apply(
        ChargeDeclined e
    )
    {
        Status = PaymentStatus.Declined;
    }

    /// <summary>May be the first event of a stream: a cancel that overtakes its charge.</summary>
    public void Apply(
        ChargeCanceled e
    )
    {
        Status = PaymentStatus.Canceled;
    }

    public void Apply(
        ChargeRefunded e
    )
    {
        Status = PaymentStatus.Refunded;
    }
}