namespace SubscriptionManagement.Features.Invoices;

public sealed record InvoiceResponse(
    Guid Id,
    Guid SubscriptionId,
    decimal Amount,
    string Currency,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    int Status,
    string StatusLabel,
    DateTime IssuedAt,
    DateTime? PaidAt)
{
    public static InvoiceResponse From(Invoice i) => new(
        i.Id, i.SubscriptionId, i.Amount, i.Currency, i.PeriodStart, i.PeriodEnd,
        (int)i.Status, i.Status.ToDb(), i.IssuedAt, i.PaidAt);
}

public sealed record InvoiceList(IReadOnlyList<InvoiceResponse> Items);
