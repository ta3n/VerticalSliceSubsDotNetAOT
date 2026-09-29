using Npgsql;
using NpgsqlTypes;
using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.Features.Invoices;

public static class InvoiceStore
{
    private const string Columns =
        "id, subscription_id, amount, currency, period_start, period_end, status, issued_at, paid_at";

    public static async Task InsertAsync(NpgsqlConnection connection, Invoice i, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"""
            INSERT INTO invoices ({Columns}) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
            """, connection);
        command.Parameters.AddWithValue(i.Id);
        command.Parameters.AddWithValue(i.SubscriptionId);
        command.Parameters.AddWithValue(i.Amount);
        command.Parameters.AddWithValue(i.Currency);
        command.Parameters.AddWithValue(i.PeriodStart);
        command.Parameters.AddWithValue(i.PeriodEnd);
        command.Parameters.AddWithValue(i.Status.ToDb());
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Timestamp, Value = Db.ToStoredUtc(i.IssuedAt) });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Timestamp, Value = i.PaidAt is { } p ? Db.ToStoredUtc(p) : DBNull.Value });
        await command.ExecuteNonQueryAsync(ct);
    }

    public static Task<Invoice?> FindAsync(NpgsqlConnection connection, Guid id, CancellationToken ct) =>
        FindCoreAsync(connection, id, forUpdate: false, ct);

    public static Task<Invoice?> FindForUpdateAsync(NpgsqlConnection connection, Guid id, CancellationToken ct) =>
        FindCoreAsync(connection, id, forUpdate: true, ct);

    public static async Task SavePaymentAsync(NpgsqlConnection connection, Invoice i, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("UPDATE invoices SET status = $2, paid_at = $3 WHERE id = $1", connection);
        command.Parameters.AddWithValue(i.Id);
        command.Parameters.AddWithValue(i.Status.ToDb());
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Timestamp, Value = i.PaidAt is { } p ? Db.ToStoredUtc(p) : DBNull.Value });
        await command.ExecuteNonQueryAsync(ct);
    }

    public static async Task<List<Invoice>> ListBySubscriptionAsync(NpgsqlConnection connection, Guid subscriptionId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            $"SELECT {Columns} FROM invoices WHERE subscription_id = $1 ORDER BY issued_at DESC, id DESC", connection);
        command.Parameters.AddWithValue(subscriptionId);
        var rows = new List<Invoice>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(Map(reader));
        }

        return rows;
    }

    private static async Task<Invoice?> FindCoreAsync(NpgsqlConnection connection, Guid id, bool forUpdate, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            $"SELECT {Columns} FROM invoices WHERE id = $1" + (forUpdate ? " FOR UPDATE" : ""), connection);
        command.Parameters.AddWithValue(id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    private static Invoice Map(NpgsqlDataReader r) => Invoice.Rehydrate(
        id: r.GetGuid(0),
        subscriptionId: r.GetGuid(1),
        amount: r.GetDecimal(2),
        currency: r.GetString(3),
        periodStart: r.GetFieldValue<DateOnly>(4),
        periodEnd: r.GetFieldValue<DateOnly>(5),
        status: InvoiceStatusNames.FromDb(r.GetString(6)),
        issuedAt: Db.FromStoredUtc(r.GetDateTime(7)),
        paidAt: r.IsDBNull(8) ? null : Db.FromStoredUtc(r.GetDateTime(8)));
}
