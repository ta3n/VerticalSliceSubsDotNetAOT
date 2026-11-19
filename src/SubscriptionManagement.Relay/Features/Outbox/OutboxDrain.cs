using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using SubscriptionManagement.Contracts;
using SubscriptionManagement.Relay.Infrastructure;
using Wolverine;
using Wolverine.EntityFrameworkCore;

namespace SubscriptionManagement.Relay.Features.Outbox;

/// <summary>
/// Moves the API's <c>outbox_messages</c> rows to the broker. A batch is one database transaction that deletes the
/// rows and stores their messages in Wolverine's durable outbox (same database, schema <c>relay</c>): either both
/// happen or neither, so a row is published at least once and never lost. Wolverine's sending agent then delivers the
/// stored envelopes to RabbitMQ and retries on its own.
/// </summary>
/// <remarks>
/// Only rows whose <c>type</c> is in <see cref="MessageTypes"/> are claimed. A row of any other type is left in the
/// table, untouched and unlocked, for a Relay version that knows it: it neither blocks the rows behind it nor is it
/// dropped. A row of a known type with an unreadable payload fails its batch, which is rolled back and retried.
/// </remarks>
public sealed class OutboxDrain(
  IServiceScopeFactory scopes,
  RelayOptions options,
  ILogger<OutboxDrain> logger,
  TimeProvider time
)
  : BackgroundService
{
  /// <summary><c>outbox_messages.type</c> to the contract record its payload deserializes to.</summary>
  internal static readonly IReadOnlyDictionary<string, Type> MessageTypes = new Dictionary<string, Type>
  {
    [OutboxMessageTypes.InvoiceIssued] = typeof(InvoiceIssued),
    [OutboxMessageTypes.SubscriptionCanceled] = typeof(SubscriptionCanceled)
  };

  // The API writes the payload with the web defaults (camelCase).
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
  private static readonly string[] KnownTypes = [.. MessageTypes.Keys];
  private static readonly ActivitySource ActivitySource = new(ServiceSetup.ActivitySourceName);

  // After a failed batch (database down, unreadable row): long enough not to flood the log.
  private static readonly TimeSpan FailureDelay = TimeSpan.FromSeconds(5);
  private DateTimeOffset _nextUnknownTypeCheck;

  protected override async Task ExecuteAsync(
    CancellationToken stoppingToken
  )
  {
    var pollInterval = TimeSpan.FromMilliseconds(options.PollIntervalMs);
    try
    {
      while (!stoppingToken.IsCancellationRequested)
      {
        TimeSpan pause;
        try
        {
          // A full batch means there is probably more: go on without a pause.
          pause = await DrainBatchAsync(stoppingToken) < options.BatchSize ? pollInterval : TimeSpan.Zero;
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
          logger.LogError(ex, "Outbox batch failed and was rolled back; retrying in {Delay}.", FailureDelay);
          pause = FailureDelay;
        }

        await Task.Delay(pause, stoppingToken);
      }
    }
    catch (Exception) when (stoppingToken.IsCancellationRequested)
    {
      // Shutdown: an interrupted batch has been rolled back and is relayed after the restart.
    }
  }

  /// <summary>Relays up to <see cref="RelayOptions.BatchSize"/> rows, oldest first, and returns how many.</summary>
  internal async Task<int> DrainBatchAsync(
    CancellationToken ct
  )
  {
    await using var scope = scopes.CreateAsyncScope();
    var outbox = scope.ServiceProvider.GetRequiredService<IDbContextOutbox<RelayDbContext>>();
    var database = outbox.DbContext.Database;

    // Disposing the transaction without a commit (empty batch, exception) rolls it back.
    await using var transaction = await database.BeginTransactionAsync(ct);
    await WarnUnknownTypesAsync(
      (NpgsqlConnection)database.GetDbConnection(),
      (NpgsqlTransaction)transaction.GetDbTransaction(),
      ct
    );
    var rows = await DeleteBatchAsync(
      (NpgsqlConnection)database.GetDbConnection(),
      (NpgsqlTransaction)transaction.GetDbTransaction(),
      ct
    );
    if (rows.Count == 0) return 0;

    foreach (var row in rows)
    {
      // Continues the trace of the request that wrote the row: Wolverine stamps the current activity on the
      // envelope as its parent, and the broker hop and the consumer carry on from there.
      using var activity = ActivitySource.StartActivity(
        $"relay {row.Type}",
        ActivityKind.Internal,
        row.Traceparent
      );
      activity?.SetTag("outbox.message.id", row.Id);

      var message = JsonSerializer.Deserialize(row.Payload, MessageTypes[row.Type], Json)
        ?? throw new JsonException($"Outbox message {row.Id} ({row.Type}) has a null payload.");
      // One bus publishes the whole batch, and its own correlation id would tie unrelated rows together.
      await outbox.PublishAsync(message, new DeliveryOptions { CorrelationId = activity?.TraceId.ToString() });
    }

    // Writes the envelopes and commits the transaction opened above, then hands the envelopes to the sending agent.
    await outbox.SaveChangesAndFlushMessagesAsync(ct);
    return rows.Count;
  }

  private async Task WarnUnknownTypesAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    CancellationToken ct
  )
  {
    var now = time.GetUtcNow();
    if (now < _nextUnknownTypeCheck) return;

    await using var command = new NpgsqlCommand(
      "SELECT count(*), min(type) FROM outbox_messages WHERE NOT (type = ANY($1))",
      connection,
      transaction
    );
    command.Parameters.AddWithValue(KnownTypes);
    await using var reader = await command.ExecuteReaderAsync(ct);
    await reader.ReadAsync(ct);
    var count = reader.GetInt64(0);
    if (count > 0)
      logger.LogWarning(
        "Outbox has {Count} rows with unknown message types (example: {MessageType}); leaving them for a compatible Relay version.",
        count,
        reader.GetString(1)
      );

    // Bound both the query and the warning to once a minute, including while known rows keep the drain busy.
    _nextUnknownTypeCheck = now.AddMinutes(1);
  }

  private async Task<List<OutboxRow>> DeleteBatchAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    CancellationToken ct
  )
  {
    // SKIP LOCKED: a second Relay instance would take the next rows instead of waiting for these.
    await using var command = new NpgsqlCommand(
      """
      DELETE FROM outbox_messages
      WHERE id IN (
          SELECT id FROM outbox_messages WHERE type = ANY($2) ORDER BY id FOR UPDATE SKIP LOCKED LIMIT $1)
      RETURNING id, type, payload, traceparent
      """,
      connection,
      transaction
    );
    command.Parameters.AddWithValue(options.BatchSize);
    command.Parameters.AddWithValue(KnownTypes);

    var rows = new List<OutboxRow>();
    await using (var reader = await command.ExecuteReaderAsync(ct))
    {
      while (await reader.ReadAsync(ct))
        rows.Add(
          new OutboxRow(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3)
          )
        );
    }

    // RETURNING gives no order; the rows are published in the order they were written.
    rows.Sort((a, b) => a.Id.CompareTo(b.Id));
    return rows;
  }

  private sealed record OutboxRow(
    long Id,
    string Type,
    string Payload,
    string? Traceparent
  );
}