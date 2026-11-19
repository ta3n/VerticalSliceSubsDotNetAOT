using System.Diagnostics;
using Npgsql;
using NpgsqlTypes;

namespace SubscriptionManagement.Infrastructure;

/// <summary>Whether the API writes messages for the other services (<c>Outbox:Enabled</c>); off unless a Relay drains the table.</summary>
public sealed record OutboxOptions(
  bool Enabled
);

/// <summary>
/// Transactional outbox: a message for the broker is a row in <c>outbox_messages</c>, written on the connection
/// (and so in the transaction) of the business change it describes. The API never talks to the broker; the Relay
/// service publishes the rows.
/// </summary>
public static class Outbox
{
  /// <param name="type">One of <c>OutboxMessageTypes</c>.</param>
  /// <param name="json">The message serialized with <see cref="AppJsonSerializerContext"/>.</param>
  public static async Task EnqueueAsync(
    NpgsqlConnection connection,
    string type,
    string json,
    CancellationToken ct
  )
  {
    await using var command = new NpgsqlCommand(
      """
      INSERT INTO outbox_messages (type, payload, traceparent, occurred_at)
      VALUES ($1, $2, $3, now() AT TIME ZONE 'UTC')
      """,
      connection
    );
    command.Parameters.AddWithValue(type);
    command.Parameters.AddWithValue(json);
    // The W3C id of the current span, so the Relay continues this request's trace (the column fits exactly that format).
    var traceparent = Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity ? activity.Id : null;
    command.Parameters.Add(
      new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Varchar, Value = (object?)traceparent ?? DBNull.Value }
    );
    await command.ExecuteNonQueryAsync(ct);
  }
}