using Npgsql;
using SubscriptionManagement.Features.Subscriptions;
using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.Features.Invoices.ListInvoicesBySubscription;

public sealed record ListInvoicesBySubscriptionQuery(Guid SubscriptionId);

public static class ListInvoicesBySubscriptionHandler
{
    /// <summary>Newest first. An unknown subscription is a 404, not an empty list.</summary>
    public static Task<InvoiceList> Handle(ListInvoicesBySubscriptionQuery query, NpgsqlDataSource db, CancellationToken ct) =>
        db.WithConnectionAsync(async (connection, token) =>
        {
            if (!await SubscriptionStore.ExistsAsync(connection, query.SubscriptionId, token))
            {
                throw SubscriptionProblems.NotFound(query.SubscriptionId);
            }

            var invoices = await InvoiceStore.ListBySubscriptionAsync(connection, query.SubscriptionId, token);
            return new InvoiceList(invoices.Select(InvoiceResponse.From).ToList());
        }, ct);
}

public static class ListInvoicesBySubscriptionEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/subscriptions/{id}/invoices", async (Guid id, NpgsqlDataSource db, CancellationToken ct) =>
            TypedResults.Ok(await ListInvoicesBySubscriptionHandler.Handle(new ListInvoicesBySubscriptionQuery(id), db, ct)));
}
