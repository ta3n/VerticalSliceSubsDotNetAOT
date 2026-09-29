using Npgsql;
using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.Features.Invoices.GetInvoice;

public sealed record GetInvoiceQuery(Guid Id);

public static class GetInvoiceHandler
{
    public static async Task<InvoiceResponse> Handle(GetInvoiceQuery query, NpgsqlDataSource db, CancellationToken ct)
    {
        var invoice = await db.WithConnectionAsync((connection, token) => InvoiceStore.FindAsync(connection, query.Id, token), ct)
            ?? throw InvoiceProblems.NotFound(query.Id);
        return InvoiceResponse.From(invoice);
    }
}

public static class GetInvoiceEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/invoices/{id}", async (Guid id, NpgsqlDataSource db, CancellationToken ct) =>
            TypedResults.Ok(await GetInvoiceHandler.Handle(new GetInvoiceQuery(id), db, ct)));
}
