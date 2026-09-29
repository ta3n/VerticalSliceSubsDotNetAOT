using Npgsql;
using SubscriptionManagement.Features.Invoices;
using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.Features.Subscriptions.RenewSubscription;

public sealed record RenewSubscriptionCommand(Guid Id);

/// <summary>The renewed subscription plus the invoice issued for the period just entered.</summary>
public sealed record RenewSubscriptionResult(SubscriptionResponse Subscription, InvoiceResponse Invoice);

public static class RenewSubscriptionHandler
{
    // Subscriptions carry no plan/currency (as in the reference): renewals bill the current price in USD.
    private const string Currency = "USD";

    public static async Task<RenewSubscriptionResult> Handle(
        RenewSubscriptionCommand command,
        NpgsqlDataSource db,
        SubscriptionCache cache,
        TimeProvider time,
        CancellationToken ct)
    {
        // Period advance and invoice insert commit together or not at all.
        var result = await db.InTransactionAsync(async (connection, token) =>
        {
            var subscription = await SubscriptionStore.FindForUpdateAsync(connection, command.Id, token)
                ?? throw SubscriptionProblems.NotFound(command.Id);
            var period = subscription.Renew();
            var invoice = Invoice.Issue(subscription.Id, subscription.Price, Currency, period, time.GetUtcNow().UtcDateTime);
            await SubscriptionStore.SaveStateAsync(connection, subscription, token);
            await InvoiceStore.InsertAsync(connection, invoice, token);
            return new RenewSubscriptionResult(SubscriptionResponse.From(subscription), InvoiceResponse.From(invoice));
        }, ct);

        await cache.PutAsync(result.Subscription, CancellationToken.None);
        return result;
    }
}
