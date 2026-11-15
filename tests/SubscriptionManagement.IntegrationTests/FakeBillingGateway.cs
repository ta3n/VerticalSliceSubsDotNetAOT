using SubscriptionManagement.Features.Subscriptions;
using SubscriptionManagement.Infrastructure.Billing;

namespace SubscriptionManagement.IntegrationTests;

/// <summary>
/// Programmable stand-in for the Billing service: records every quote request, quotes price plus 10 % tax unless
/// <see cref="Quote"/> is replaced (e.g. to throw), and answers renewal lookups from <see cref="Renewals"/>.
/// </summary>
public sealed class FakeBillingGateway : IBillingGateway
{
    public sealed record QuoteCall(
        Guid SubscriptionId,
        decimal Price,
        string Currency,
        BilledPeriod Period
    );

    private static readonly Func<QuoteCall, RenewalQuote> PricePlusTenPercent =
        call => new RenewalQuote(call.Price, call.Price * 0.10m, call.Price * 1.10m);

    public List<QuoteCall> QuoteCalls { get; } = [];
    public Func<QuoteCall, RenewalQuote> Quote { get; set; } = PricePlusTenPercent;
    public Dictionary<Guid, RenewalStatus> Renewals { get; } = [];

    /// <summary>Back to the defaults; the instance is shared by every test of the run.</summary>
    public void Reset()
    {
        QuoteCalls.Clear();
        Quote = PricePlusTenPercent;
        Renewals.Clear();
    }

    public Task<RenewalQuote> GetRenewalQuoteAsync(
        Guid subscriptionId,
        decimal price,
        string currency,
        BilledPeriod period,
        CancellationToken ct
    )
    {
        var call = new QuoteCall(subscriptionId, price, currency, period);
        QuoteCalls.Add(call);
        return Task.FromResult(Quote(call));
    }

    public Task<RenewalStatus> GetRenewalAsync(
        Guid invoiceId,
        CancellationToken ct
    )
    {
        return Renewals.TryGetValue(invoiceId, out var renewal)
            ? Task.FromResult(renewal)
            : throw BillingProblems.RenewalNotFound(invoiceId);
    }
}