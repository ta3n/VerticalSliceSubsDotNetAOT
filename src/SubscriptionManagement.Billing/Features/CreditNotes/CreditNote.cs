using SubscriptionManagement.Contracts;

namespace SubscriptionManagement.Billing.Features.CreditNotes;

/// <summary>Billing document for a refunded payment. At most one per payment (<c>payment_id</c> is unique).</summary>
public sealed class CreditNote
{
    public Guid Id { get; init; }
    public Guid PaymentId { get; init; }
    public Guid InvoiceId { get; init; }
    public Guid SubscriptionId { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "";
    public string Reason { get; init; } = "";
    public DateTimeOffset IssuedAt { get; init; }

    public static CreditNote Issue(
        PaymentRefunded refunded,
        DateTimeOffset now
    )
    {
        return new CreditNote
        {
            Id = Guid.NewGuid(),
            PaymentId = refunded.PaymentId,
            InvoiceId = refunded.InvoiceId,
            SubscriptionId = refunded.SubscriptionId,
            Amount = refunded.Amount,
            Currency = refunded.Currency,
            Reason = refunded.Reason,
            IssuedAt = now
        };
    }
}

public sealed record CreditNoteResponse(
    Guid Id,
    Guid PaymentId,
    Guid InvoiceId,
    Guid SubscriptionId,
    decimal Amount,
    string Currency,
    string Reason,
    DateTimeOffset IssuedAt
)
{
    public static CreditNoteResponse From(
        CreditNote c
    )
    {
        return new CreditNoteResponse(
            c.Id,
            c.PaymentId,
            c.InvoiceId,
            c.SubscriptionId,
            c.Amount,
            c.Currency,
            c.Reason,
            c.IssuedAt
        );
    }
}

public sealed record CreditNoteList(
    IReadOnlyList<CreditNoteResponse> Items
);