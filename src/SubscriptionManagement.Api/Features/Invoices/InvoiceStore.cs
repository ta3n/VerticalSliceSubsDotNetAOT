using Npgsql;
using NpgsqlTypes;
using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.Features.Invoices;

public static class InvoiceStore
{
  private const string Columns =
    "id, subscription_id, amount, currency, period_start, period_end, status, issued_at, paid_at, payment_id";

  public static async Task InsertAsync(
    NpgsqlConnection connection,
    Invoice i,
    CancellationToken ct
  )
  {
    await using var command = new NpgsqlCommand(
      $"""
      INSERT INTO invoices ({Columns}) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)
      """,
      connection
    );
    command.Parameters.AddWithValue(i.Id);
    command.Parameters.AddWithValue(i.SubscriptionId);
    command.Parameters.AddWithValue(i.Amount);
    command.Parameters.AddWithValue(i.Currency);
    command.Parameters.AddWithValue(i.PeriodStart);
    command.Parameters.AddWithValue(i.PeriodEnd);
    command.Parameters.AddWithValue(i.Status.ToDb());
    command.Parameters.Add(
      new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Timestamp, Value = Db.ToStoredUtc(i.IssuedAt) }
    );
    command.Parameters.Add(
      new NpgsqlParameter
        { NpgsqlDbType = NpgsqlDbType.Timestamp, Value = i.PaidAt is { } p ? Db.ToStoredUtc(p) : DBNull.Value }
    );
    command.Parameters.Add(
      new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = (object?)i.PaymentId ?? DBNull.Value }
    );
    await command.ExecuteNonQueryAsync(ct);
  }

  public static Task<Invoice?> FindAsync(
    NpgsqlConnection connection,
    Guid id,
    CancellationToken ct
  )
  {
    return FindCoreAsync(connection, id, false, ct);
  }

  public static Task<Invoice?> FindForUpdateAsync(
    NpgsqlConnection connection,
    Guid id,
    CancellationToken ct
  )
  {
    return FindCoreAsync(connection, id, true, ct);
  }

  public static async Task SavePaymentAsync(
    NpgsqlConnection connection,
    Invoice i,
    CancellationToken ct
  )
  {
    await using var command = new NpgsqlCommand(
      "UPDATE invoices SET status = $2, paid_at = $3 WHERE id = $1",
      connection
    );
    command.Parameters.AddWithValue(i.Id);
    command.Parameters.AddWithValue(i.Status.ToDb());
    command.Parameters.Add(
      new NpgsqlParameter
        { NpgsqlDbType = NpgsqlDbType.Timestamp, Value = i.PaidAt is { } p ? Db.ToStoredUtc(p) : DBNull.Value }
    );
    await command.ExecuteNonQueryAsync(ct);
  }

  public static async Task SaveSettlementAsync(
    NpgsqlConnection connection,
    Invoice i,
    CancellationToken ct
  )
  {
    await using var command = new NpgsqlCommand(
      "UPDATE invoices SET status = $2, paid_at = $3, payment_id = $4 WHERE id = $1",
      connection
    );
    command.Parameters.AddWithValue(i.Id);
    command.Parameters.AddWithValue(i.Status.ToDb());
    command.Parameters.Add(
      new NpgsqlParameter
        { NpgsqlDbType = NpgsqlDbType.Timestamp, Value = i.PaidAt is { } p ? Db.ToStoredUtc(p) : DBNull.Value }
    );
    command.Parameters.Add(
      new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = (object?)i.PaymentId ?? DBNull.Value }
    );
    await command.ExecuteNonQueryAsync(ct);
  }

  public static async Task SaveVoidAsync(
    NpgsqlConnection connection,
    Invoice i,
    CancellationToken ct
  )
  {
    await using var command = new NpgsqlCommand("UPDATE invoices SET status = $2 WHERE id = $1", connection);
    command.Parameters.AddWithValue(i.Id);
    command.Parameters.AddWithValue(i.Status.ToDb());
    await command.ExecuteNonQueryAsync(ct);
  }

  /// <summary>True when a payment was collected for any invoice of the subscription (settled, not paid by hand).</summary>
  public static async Task<bool> HasCollectedPaymentAsync(
    NpgsqlConnection connection,
    Guid subscriptionId,
    CancellationToken ct
  )
  {
    await using var command = new NpgsqlCommand(
      "SELECT EXISTS (SELECT 1 FROM invoices WHERE subscription_id = $1 AND payment_id IS NOT NULL)",
      connection
    );
    command.Parameters.AddWithValue(subscriptionId);
    return (bool)(await command.ExecuteScalarAsync(ct))!;
  }

  public static async Task<List<Invoice>> ListBySubscriptionAsync(
    NpgsqlConnection connection,
    Guid subscriptionId,
    CancellationToken ct
  )
  {
    await using var command = new NpgsqlCommand(
      $"SELECT {Columns} FROM invoices WHERE subscription_id = $1 ORDER BY issued_at DESC, id DESC",
      connection
    );
    command.Parameters.AddWithValue(subscriptionId);
    var rows = new List<Invoice>();
    await using var reader = await command.ExecuteReaderAsync(ct);
    while (await reader.ReadAsync(ct)) rows.Add(Map(reader));

    return rows;
  }

  private static async Task<Invoice?> FindCoreAsync(
    NpgsqlConnection connection,
    Guid id,
    bool forUpdate,
    CancellationToken ct
  )
  {
    await using var command = new NpgsqlCommand(
      $"SELECT {Columns} FROM invoices WHERE id = $1" + (forUpdate ? " FOR UPDATE" : ""),
      connection
    );
    command.Parameters.AddWithValue(id);
    await using var reader = await command.ExecuteReaderAsync(ct);
    return await reader.ReadAsync(ct) ? Map(reader) : null;
  }

  private static Invoice Map(
    NpgsqlDataReader r
  )
  {
    return Invoice.Rehydrate(
      r.GetGuid(0),
      r.GetGuid(1),
      r.GetDecimal(2),
      r.GetString(3),
      r.GetFieldValue<DateOnly>(4),
      r.GetFieldValue<DateOnly>(5),
      InvoiceStatusNames.FromDb(r.GetString(6)),
      Db.FromStoredUtc(r.GetDateTime(7)),
      r.IsDBNull(8) ? null : Db.FromStoredUtc(r.GetDateTime(8)),
      r.IsDBNull(9) ? null : r.GetGuid(9)
    );
  }
}