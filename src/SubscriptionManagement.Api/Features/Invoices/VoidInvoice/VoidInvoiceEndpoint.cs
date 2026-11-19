using System.Diagnostics;
using Npgsql;
using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.Features.Invoices.VoidInvoice;

public sealed record VoidInvoiceCommand(
  Guid Id
);

public static class VoidInvoiceHandler
{
  /// <summary>
  /// Cancels an unpaid invoice: the compensation of a renewal whose payment was declined or timed out. The row
  /// lock decides a race with a payment; a repeated void is a no-op.
  /// </summary>
  public static Task<InvoiceResponse> Handle(
    VoidInvoiceCommand command,
    NpgsqlDataSource db,
    CancellationToken ct
  )
  {
    return db.InTransactionAsync(
      async (connection, token) =>
      {
        var invoice = await InvoiceStore.FindForUpdateAsync(connection, command.Id, token)
          ?? throw InvoiceProblems.NotFound(command.Id);
        invoice.Void();
        await InvoiceStore.SaveVoidAsync(connection, invoice, token);
        return InvoiceResponse.From(invoice);
      },
      ct
    );
  }
}

public static class VoidInvoiceEndpoint
{
  public static void Map(
    IEndpointRouteBuilder app
  )
  {
    app.MapPost(
      "/invoices/{id}/void",
      async (Guid id, NpgsqlDataSource db, CancellationToken ct) =>
      {
        Activity.Current?.SetTag("invoice.id", id.ToString());
        return TypedResults.Ok(await VoidInvoiceHandler.Handle(new VoidInvoiceCommand(id), db, ct));
      }
    );
  }
}