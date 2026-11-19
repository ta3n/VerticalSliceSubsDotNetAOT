using Npgsql;

namespace SubscriptionManagement.Reporting.Features.Revenue;

/// <summary>
/// The read model: one table per kind of fact, keyed by the fact's natural id, so a message delivered twice (Kafka is
/// at-least-once) or out of order (payment before invoice) ends in the same rows and the summary is a plain query.
/// </summary>
internal static class RevenueSchema
{
    public const string Ddl =
        """
        CREATE TABLE IF NOT EXISTS invoices (
            invoice_id      UUID PRIMARY KEY,
            subscription_id UUID           NOT NULL,
            amount          NUMERIC(19, 4) NOT NULL,
            currency        VARCHAR(3)     NOT NULL,
            period_start    DATE           NOT NULL,
            period_end      DATE           NOT NULL
        );
        CREATE TABLE IF NOT EXISTS payments (
            payment_id UUID PRIMARY KEY,
            invoice_id UUID        NOT NULL,
            outcome    VARCHAR(16) NOT NULL,
            reason     TEXT        NULL
        );
        CREATE TABLE IF NOT EXISTS refunds (
            payment_id      UUID PRIMARY KEY,
            invoice_id      UUID           NOT NULL,
            subscription_id UUID           NOT NULL,
            amount          NUMERIC(19, 4) NOT NULL,
            currency        VARCHAR(3)     NOT NULL,
            reason          TEXT           NOT NULL
        );
        CREATE TABLE IF NOT EXISTS cancellations (
            subscription_id UUID PRIMARY KEY,
            canceled_on     DATE NOT NULL
        );
        """;
}

/// <summary>Creates the tables before Wolverine starts listening (hosted services start in registration order).</summary>
internal sealed class RevenueSchemaInitializer(
    NpgsqlDataSource dataSource
) : IHostedService
{
    public async Task StartAsync(
        CancellationToken ct
    )
    {
        await using var command = dataSource.CreateCommand(RevenueSchema.Ddl);
        await command.ExecuteNonQueryAsync(ct);
    }

    public Task StopAsync(
        CancellationToken ct
    )
    {
        return Task.CompletedTask;
    }
}