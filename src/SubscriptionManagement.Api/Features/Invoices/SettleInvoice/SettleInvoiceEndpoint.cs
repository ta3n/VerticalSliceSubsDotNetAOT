using System.Diagnostics;
using System.Text.Json;
using Npgsql;
using SubscriptionManagement.Contracts;
using SubscriptionManagement.Features.Subscriptions;
using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.Features.Invoices.SettleInvoice;

/// <summary>Request body. Nullable so validation reports "missing" instead of binding an empty id.</summary>
public sealed record SettleInvoiceRequest(
    Guid? PaymentId
);

public sealed record SettleInvoiceCommand(
    Guid Id,
    Guid? PaymentId
);

public static class SettleInvoiceValidator
{
    public static void Validate(
        SettleInvoiceCommand command
    )
    {
        var errors = new ValidationErrors();
        if (command.PaymentId is null)
        {
            errors.Add("paymentId", "must not be null");
        }

        errors.ThrowIfAny();
    }
}

public static class SettleInvoiceHandler
{
    /// <summary>
    /// Marks the invoice paid by a payment collected in the Payments service. Called by the Relay for the renewal
    /// saga, which may deliver the command more than once: the row lock and the stored payment id make a repeat a no-op.
    /// </summary>
    public static Task<InvoiceResponse> Handle(
        SettleInvoiceCommand command,
        NpgsqlDataSource db,
        TimeProvider time,
        OutboxOptions outbox,
        CancellationToken ct
    )
    {
        SettleInvoiceValidator.Validate(command);

        return db.InTransactionAsync(
            async (connection, token) =>
            {
                var existing = await InvoiceStore.FindAsync(connection, command.Id, token)
                    ?? throw InvoiceProblems.NotFound(command.Id);
                // Serialize cancellation and settlement so a capture arriving after cancel still starts the refund.
                var subscription =
                    await SubscriptionStore.FindForUpdateAsync(connection, existing.SubscriptionId, token)
                    ?? throw SubscriptionProblems.NotFound(existing.SubscriptionId);
                var invoice = await InvoiceStore.FindForUpdateAsync(connection, command.Id, token)
                    ?? throw InvoiceProblems.NotFound(command.Id);
                var wasOpen = invoice.Status == InvoiceStatus.Open;
                invoice.Settle(command.PaymentId!.Value, time.GetUtcNow().UtcDateTime);
                await InvoiceStore.SaveSettlementAsync(connection, invoice, token);
                if (wasOpen && outbox.Enabled && subscription.Status == SubscriptionStatus.Canceled)
                {
                    var canceled = new SubscriptionCanceled(
                        subscription.Id,
                        DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime)
                    );
                    await Outbox.EnqueueAsync(
                        connection,
                        OutboxMessageTypes.SubscriptionCanceled,
                        JsonSerializer.Serialize(canceled, AppJsonSerializerContext.Default.SubscriptionCanceled),
                        token
                    );
                }

                return InvoiceResponse.From(invoice);
            },
            ct
        );
    }
}

public static class SettleInvoiceEndpoint
{
    public static void Map(
        IEndpointRouteBuilder app
    )
    {
        app.MapPost(
            "/invoices/{id}/settle",
            async (
                Guid id, SettleInvoiceRequest request, NpgsqlDataSource db, TimeProvider time, OutboxOptions outbox,
                CancellationToken ct
            ) =>
            {
                Activity.Current?.SetTag("invoice.id", id.ToString());
                return TypedResults.Ok(
                    await SettleInvoiceHandler.Handle(
                        new SettleInvoiceCommand(id, request.PaymentId),
                        db,
                        time,
                        outbox,
                        ct
                    )
                );
            }
        );
    }
}