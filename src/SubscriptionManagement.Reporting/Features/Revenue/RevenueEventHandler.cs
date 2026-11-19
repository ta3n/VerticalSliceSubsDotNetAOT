using Npgsql;
using SubscriptionManagement.Contracts;

namespace SubscriptionManagement.Reporting.Features.Revenue;

/// <summary>
/// Projects the facts of the <see cref="KafkaTopics.SubscriptionEvents" /> topic into the revenue tables. Every handler
/// is an upsert on the fact's natural id, so redelivery and reordering are harmless.
/// </summary>
public static class RevenueEventHandler
{
    public static async Task Handle(
        InvoiceIssued message,
        NpgsqlDataSource db,
        CancellationToken ct
    )
    {
        await using var command = db.CreateCommand(
            """
            INSERT INTO invoices (invoice_id, subscription_id, amount, currency, period_start, period_end)
            VALUES ($1, $2, $3, $4, $5, $6)
            ON CONFLICT (invoice_id) DO NOTHING
            """
        );
        command.Parameters.AddWithValue(message.InvoiceId);
        command.Parameters.AddWithValue(message.SubscriptionId);
        command.Parameters.AddWithValue(message.Amount);
        command.Parameters.AddWithValue(message.Currency);
        command.Parameters.AddWithValue(message.PeriodStart);
        command.Parameters.AddWithValue(message.PeriodEnd);
        await command.ExecuteNonQueryAsync(ct);
    }

    public static Task Handle(
        PaymentSucceeded message,
        NpgsqlDataSource db,
        CancellationToken ct
    )
    {
        return RecordPaymentAsync(db, message.PaymentId, message.RenewalSagaId, "succeeded", null, ct);
    }

    public static Task Handle(
        PaymentFailed message,
        NpgsqlDataSource db,
        CancellationToken ct
    )
    {
        return RecordPaymentAsync(db, message.PaymentId, message.RenewalSagaId, "failed", message.Reason, ct);
    }

    public static async Task Handle(
        PaymentRefunded message,
        NpgsqlDataSource db,
        CancellationToken ct
    )
    {
        await using var command = db.CreateCommand(
            """
            INSERT INTO refunds (payment_id, invoice_id, subscription_id, amount, currency, reason)
            VALUES ($1, $2, $3, $4, $5, $6)
            ON CONFLICT (payment_id) DO NOTHING
            """
        );
        command.Parameters.AddWithValue(message.PaymentId);
        command.Parameters.AddWithValue(message.InvoiceId);
        command.Parameters.AddWithValue(message.SubscriptionId);
        command.Parameters.AddWithValue(message.Amount);
        command.Parameters.AddWithValue(message.Currency);
        command.Parameters.AddWithValue(message.Reason);
        await command.ExecuteNonQueryAsync(ct);
    }

    public static async Task Handle(
        SubscriptionCanceled message,
        NpgsqlDataSource db,
        CancellationToken ct
    )
    {
        await using var command = db.CreateCommand(
            """
            INSERT INTO cancellations (subscription_id, canceled_on) VALUES ($1, $2)
            ON CONFLICT (subscription_id) DO NOTHING
            """
        );
        command.Parameters.AddWithValue(message.SubscriptionId);
        command.Parameters.AddWithValue(message.CanceledOn);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task RecordPaymentAsync(
        NpgsqlDataSource db,
        Guid paymentId,
        Guid invoiceId,
        string outcome,
        string? reason,
        CancellationToken ct
    )
    {
        // The last outcome of a payment wins.
        await using var command = db.CreateCommand(
            """
            INSERT INTO payments (payment_id, invoice_id, outcome, reason) VALUES ($1, $2, $3, $4)
            ON CONFLICT (payment_id) DO UPDATE SET outcome = EXCLUDED.outcome, reason = EXCLUDED.reason
            """
        );
        command.Parameters.AddWithValue(paymentId);
        command.Parameters.AddWithValue(invoiceId);
        command.Parameters.AddWithValue(outcome);
        command.Parameters.AddWithValue(reason ?? (object)DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }
}