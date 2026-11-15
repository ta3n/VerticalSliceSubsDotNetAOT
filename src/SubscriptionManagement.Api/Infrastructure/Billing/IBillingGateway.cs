using SubscriptionManagement.Features.Subscriptions;

namespace SubscriptionManagement.Infrastructure.Billing;

/// <summary>What Billing charges for one renewal period: the price plus tax.</summary>
public sealed record RenewalQuote(
    decimal Net,
    decimal Tax,
    decimal Total
);

/// <summary>State of the saga in Billing that collects one renewal invoice.</summary>
public sealed record RenewalStatus(
    Guid InvoiceId,
    Guid? PaymentId,
    string State,
    string? FailureReason,
    DateTime? UpdatedAt
);

/// <summary>The Billing service as this API uses it. Failures surface as <see cref="BillingProblems"/>.</summary>
public interface IBillingGateway
{
    Task<RenewalQuote> GetRenewalQuoteAsync(
        Guid subscriptionId,
        decimal price,
        string currency,
        BilledPeriod period,
        CancellationToken ct
    );

    Task<RenewalStatus> GetRenewalAsync(
        Guid invoiceId,
        CancellationToken ct
    );
}