using Npgsql;
using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.Features.Invoices.PayInvoice;

public sealed record PayInvoiceCommand(Guid Id);

public static class PayInvoiceHandler
{
    /// <summary>Row-locks the invoice so two concurrent payments cannot both succeed.</summary>
    public static Task<InvoiceResponse> Handle(PayInvoiceCommand command, NpgsqlDataSource db, TimeProvider time, CancellationToken ct) =>
        db.InTransactionAsync(async (connection, token) =>
        {
            var invoice = await InvoiceStore.FindForUpdateAsync(connection, command.Id, token)
                ?? throw InvoiceProblems.NotFound(command.Id);
            invoice.Pay(time.GetUtcNow().UtcDateTime);
            await InvoiceStore.SavePaymentAsync(connection, invoice, token);
            return InvoiceResponse.From(invoice);
        }, ct);
}

public static class PayInvoiceEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/invoices/{id}/pay", async (Guid id, NpgsqlDataSource db, TimeProvider time, CancellationToken ct) =>
            TypedResults.Ok(await PayInvoiceHandler.Handle(new PayInvoiceCommand(id), db, time, ct)));
}
