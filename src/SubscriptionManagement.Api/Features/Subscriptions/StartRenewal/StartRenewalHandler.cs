using System.Text.Json;
using Npgsql;
using SubscriptionManagement.Contracts;
using SubscriptionManagement.Features.Invoices;
using SubscriptionManagement.Features.Subscriptions.RenewSubscription;
using SubscriptionManagement.Infrastructure;
using SubscriptionManagement.Infrastructure.Billing;

namespace SubscriptionManagement.Features.Subscriptions.StartRenewal;

public sealed record StartRenewalCommand(
    Guid Id
);

public static class StartRenewalHandler
{
    // Subscriptions carry no plan/currency (as in the reference): renewals bill the current price in USD.
    private const string Currency = "USD";

    /// <summary>
    /// Renews like <c>/renew</c>, but the invoice is for Billing's quote (price plus tax) and is collected by the
    /// renewal saga instead of a manual <c>/pay</c>: the InvoiceIssued message that starts the saga is written to
    /// the outbox in the same transaction as the invoice.
    /// </summary>
    public static async Task<RenewSubscriptionResult> Handle(
        StartRenewalCommand command,
        NpgsqlDataSource db,
        SubscriptionCache cache,
        IBillingGateway billing,
        OutboxOptions outbox,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (!outbox.Enabled)
            throw BillingProblems.NotConfigured(); // without a Relay the invoice would never be collected

        // Quote before the transaction so no row lock is held across the remote call. The unlocked read may be
        // stale by the time the row is locked, which is fine: the quote depends only on the price, which never changes.
        var candidate = await db.WithConnectionAsync(
                (connection, token) => SubscriptionStore.FindAsync(connection, command.Id, token),
                ct
            )
            ?? throw SubscriptionProblems.NotFound(command.Id);
        var quotedPeriod = candidate.Renew(); // on the unlocked copy: rejects a subscription that is not active
        var quote = await billing.GetRenewalQuoteAsync(candidate.Id, candidate.Price, Currency, quotedPeriod, ct);

        // Period advance, invoice and outbox message commit together or not at all.
        var result = await db.InTransactionAsync(
            async (connection, token) =>
            {
                var subscription = await SubscriptionStore.FindForUpdateAsync(connection, command.Id, token)
                    ?? throw SubscriptionProblems.NotFound(command.Id);
                var period = subscription.Renew();
                var invoice = Invoice.Issue(
                    subscription.Id,
                    quote.Total,
                    Currency,
                    period,
                    time.GetUtcNow().UtcDateTime
                );
                await SubscriptionStore.SaveStateAsync(connection, subscription, token);
                await InvoiceStore.InsertAsync(connection, invoice, token);
                var issued = new InvoiceIssued(
                    invoice.Id,
                    invoice.SubscriptionId,
                    invoice.Amount,
                    invoice.Currency,
                    invoice.PeriodStart,
                    invoice.PeriodEnd
                );
                await Outbox.EnqueueAsync(
                    connection,
                    OutboxMessageTypes.InvoiceIssued,
                    JsonSerializer.Serialize(issued, AppJsonSerializerContext.Default.InvoiceIssued),
                    token
                );
                return new RenewSubscriptionResult(
                    SubscriptionResponse.From(subscription),
                    InvoiceResponse.From(invoice)
                );
            },
            ct
        );

        await cache.PutAsync(result.Subscription, CancellationToken.None);
        return result;
    }
}