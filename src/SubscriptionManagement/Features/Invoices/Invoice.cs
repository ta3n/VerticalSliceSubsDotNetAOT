using SubscriptionManagement.Features.Subscriptions;
using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.Features.Invoices;

/// <summary>Numeric values are the wire contract.</summary>
public enum InvoiceStatus
{
    Open = 1,
    Paid = 2,
    Void = 3
}

public static class InvoiceStatusNames
{
    public static string ToDb(this InvoiceStatus status) => status switch
    {
        InvoiceStatus.Open => "OPEN",
        InvoiceStatus.Paid => "PAID",
        InvoiceStatus.Void => "VOID",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    public static InvoiceStatus FromDb(string value) => value switch
    {
        "OPEN" => InvoiceStatus.Open,
        "PAID" => InvoiceStatus.Paid,
        "VOID" => InvoiceStatus.Void,
        _ => throw new InvalidOperationException($"Unknown invoice status '{value}' in database.")
    };
}

public static class InvoiceProblems
{
    public static ApiProblemException NotFound(Guid id) => new(
        StatusCodes.Status404NotFound, "invoice-not-found", "Invoice not found",
        $"Invoice {id} was not found.",
        new Dictionary<string, object> { ["invoiceId"] = id.ToString() });

    public static ApiProblemException NotPayable(Guid id, InvoiceStatus status) => new(
        StatusCodes.Status409Conflict, "invoice-not-payable", "Invoice is not payable",
        $"Invoice {id} is {status.ToDb()}.",
        new Dictionary<string, object> { ["invoiceId"] = id.ToString(), ["invoiceStatus"] = status.ToDb() });
}

/// <summary>Billing document issued when a subscription renews a paid period.</summary>
public sealed class Invoice
{
    public Guid Id { get; }
    public Guid SubscriptionId { get; }
    public decimal Amount { get; }
    public string Currency { get; }
    public DateOnly PeriodStart { get; }
    public DateOnly PeriodEnd { get; }
    public InvoiceStatus Status { get; private set; }
    public DateTime IssuedAt { get; }
    public DateTime? PaidAt { get; private set; }

    private Invoice(
        Guid id, Guid subscriptionId, decimal amount, string currency, DateOnly periodStart, DateOnly periodEnd,
        InvoiceStatus status, DateTime issuedAt, DateTime? paidAt)
    {
        Id = id;
        SubscriptionId = subscriptionId;
        Amount = amount;
        Currency = currency;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        Status = status;
        IssuedAt = issuedAt;
        PaidAt = paidAt;
    }

    public static Invoice Issue(Guid subscriptionId, decimal amount, string currency, BilledPeriod period, DateTime nowUtc) => new(
        Guid.NewGuid(), subscriptionId,
        // Match NUMERIC(20,4) (PostgreSQL rounds half away from zero) so the response equals what is stored.
        decimal.Round(amount, 4, MidpointRounding.AwayFromZero),
        currency, period.Start, period.End, InvoiceStatus.Open, Truncate(nowUtc), null);

    public static Invoice Rehydrate(
        Guid id, Guid subscriptionId, decimal amount, string currency, DateOnly periodStart, DateOnly periodEnd,
        InvoiceStatus status, DateTime issuedAt, DateTime? paidAt) =>
        new(id, subscriptionId, amount, currency, periodStart, periodEnd, status, issuedAt, paidAt);

    /// <summary>A paid or void invoice cannot be paid again.</summary>
    public void Pay(DateTime nowUtc)
    {
        if (Status != InvoiceStatus.Open)
        {
            throw InvoiceProblems.NotPayable(Id, Status);
        }

        Status = InvoiceStatus.Paid;
        PaidAt = Truncate(nowUtc);
    }

    // PostgreSQL timestamps keep microseconds; .NET ticks are finer.
    private static DateTime Truncate(DateTime value) => new(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);
}
