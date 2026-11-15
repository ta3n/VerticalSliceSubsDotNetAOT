using SubscriptionManagement.Features.Subscriptions;

namespace SubscriptionManagement.Infrastructure.Billing;

/// <summary>Registered when <c>Billing:GrpcAddress</c> is empty: every call is a 503, with no DNS lookup or retry.</summary>
public sealed class UnconfiguredBillingGateway : IBillingGateway
{
    public Task<RenewalQuote> GetRenewalQuoteAsync(
        Guid subscriptionId,
        decimal price,
        string currency,
        BilledPeriod period,
        CancellationToken ct
    )
    {
        throw BillingProblems.NotConfigured();
    }

    public Task<RenewalStatus> GetRenewalAsync(
        Guid invoiceId,
        CancellationToken ct
    )
    {
        throw BillingProblems.NotConfigured();
    }
}