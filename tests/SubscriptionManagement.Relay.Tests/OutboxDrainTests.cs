using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using SubscriptionManagement.Contracts;
using SubscriptionManagement.Relay.Features.Outbox;
using Wolverine.Tracking;

namespace SubscriptionManagement.Relay.Tests;

/// <summary>
/// The outbox drain against a real <c>outbox_messages</c> table. "Published" is what Wolverine's tracking saw leave
/// for RabbitMQ; "stored" is what reached the durable outbox table (recorded by a trigger, see the fixture).
/// </summary>
[Collection(RelayCollection.Name)]
public class OutboxDrainTests(
  RelayFixture fixture
) : IAsyncLifetime
{
  private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

  public Task InitializeAsync()
  {
    return fixture.ExecuteAsync("TRUNCATE outbox_messages, stored_envelopes");
  }

  public Task DisposeAsync()
  {
    return Task.CompletedTask;
  }

  [Fact]
  public async Task A_batch_publishes_every_row_once_in_id_order_and_deletes_it()
  {
    var invoiceId = Guid.NewGuid();
    var subscriptionId = Guid.NewGuid();
    // Payloads as the API writes them: web defaults, camelCase.
    await EnqueueAsync(
      OutboxMessageTypes.InvoiceIssued,
      $$"""{"invoiceId":"{{invoiceId}}","subscriptionId":"{{subscriptionId}}","amount":11.00,"currency":"USD","periodStart":"2026-01-01","periodEnd":"2026-02-01"}"""
    );
    await EnqueueAsync(
      OutboxMessageTypes.SubscriptionCanceled,
      $$"""{"subscriptionId":"{{subscriptionId}}","canceledOn":"2026-01-15"}"""
    );
    var second = new InvoiceIssued(
      Guid.NewGuid(),
      subscriptionId,
      6000m,
      "EUR",
      new DateOnly(2026, 2, 1),
      new DateOnly(2026, 3, 1)
    );
    await EnqueueAsync(second);

    var drain = fixture.CreateDrain();
    var relayed = 0;
    var session = await TrackAsync(async () => relayed = await drain.DrainBatchAsync(default));

    Assert.Equal(3, relayed);
    Assert.Equal(
      [
        new InvoiceIssued(
          invoiceId,
          subscriptionId,
          11.00m,
          "USD",
          new DateOnly(2026, 1, 1),
          new DateOnly(2026, 2, 1)
        ),
        new SubscriptionCanceled(subscriptionId, new DateOnly(2026, 1, 15)),
        second
      ],
      session.Sent.AllMessages().ToArray()
    );
    // Conventional routing: one exchange per message type, named after the type.
    Assert.Equal(
      [
        "rabbitmq://exchange/SubscriptionManagement.Contracts.InvoiceIssued",
        "rabbitmq://exchange/SubscriptionManagement.Contracts.SubscriptionCanceled",
        "rabbitmq://exchange/SubscriptionManagement.Contracts.InvoiceIssued"
      ],
      session.Sent.Envelopes().Select(e => e.Destination!.ToString()).ToArray()
    );
    // Every published envelope went through the durable outbox table first.
    Assert.Equal(
      session.Sent.Envelopes().Select(e => e.Id).Order().ToArray(),
      (await StoredEnvelopeIdsAsync()).Order().ToArray()
    );
    Assert.Equal(0, await OutboxCountAsync());

    // Nothing is left to relay, so nothing is published twice.
    var again = await TrackAsync(async () => relayed = await drain.DrainBatchAsync(default));
    Assert.Equal(0, relayed);
    Assert.Empty(again.Sent.AllMessages());
  }

  [Fact]
  public async Task A_batch_takes_the_oldest_rows_up_to_the_batch_size()
  {
    var facts = Enumerable.Range(1, 5)
      .Select(day => new SubscriptionCanceled(Guid.NewGuid(), new DateOnly(2026, 1, day)))
      .ToArray();
    foreach (var fact in facts) await EnqueueAsync(fact);

    var drain = fixture.CreateDrain(2);
    var relayed = 0;
    var session = await TrackAsync(async () => relayed = await drain.DrainBatchAsync(default));

    Assert.Equal(2, relayed);
    Assert.Equal(facts[..2], session.Sent.MessagesOf<SubscriptionCanceled>().ToArray());
    Assert.Equal(3, await OutboxCountAsync());
  }

  [Fact]
  public async Task An_unreadable_row_fails_its_batch_and_nothing_is_deleted_stored_or_published()
  {
    await EnqueueAsync(new SubscriptionCanceled(Guid.NewGuid(), new DateOnly(2026, 1, 15)));
    await EnqueueAsync(OutboxMessageTypes.InvoiceIssued, "{ not json");
    await EnqueueAsync(new SubscriptionCanceled(Guid.NewGuid(), new DateOnly(2026, 1, 16)));

    var drain = fixture.CreateDrain();
    var session = await TrackAsync(() => Assert.ThrowsAsync<JsonException>(() => drain.DrainBatchAsync(default)));

    // The row before the unreadable one had already been deleted and published inside the transaction.
    Assert.Equal(3, await OutboxCountAsync());
    Assert.Empty(await StoredEnvelopeIdsAsync());
    Assert.Empty(session.Sent.AllMessages());
  }

  /// <summary>
  /// The deletes and the envelope inserts are one transaction: when its commit fails, after both have run, neither
  /// survives. A deferred constraint trigger makes exactly the COMMIT fail.
  /// </summary>
  [Fact]
  public async Task A_failed_commit_keeps_the_rows_and_stores_no_envelope_and_the_retry_publishes_them_once()
  {
    var facts = new[]
    {
      new SubscriptionCanceled(Guid.NewGuid(), new DateOnly(2026, 1, 15)),
      new SubscriptionCanceled(Guid.NewGuid(), new DateOnly(2026, 1, 16))
    };
    await EnqueueAsync(facts[0]);
    await EnqueueAsync(facts[1]);
    await fixture.ExecuteAsync(
      """
      CREATE FUNCTION reject_commit() RETURNS trigger LANGUAGE plpgsql AS
          $$ BEGIN RAISE EXCEPTION 'commit rejected by the test'; END $$;
      CREATE CONSTRAINT TRIGGER reject_commit AFTER DELETE ON outbox_messages
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION reject_commit();
      """
    );

    var drain = fixture.CreateDrain();
    ITrackedSession failed;
    try
    {
      failed = await TrackAsync(
        async () =>
        {
          var rejected = await Assert.ThrowsAsync<PostgresException>(() => drain.DrainBatchAsync(default));
          Assert.Contains("commit rejected by the test", rejected.MessageText);
        }
      );
    }
    finally
    {
      await fixture.ExecuteAsync("DROP TRIGGER reject_commit ON outbox_messages; DROP FUNCTION reject_commit();");
    }

    Assert.Equal(2, await OutboxCountAsync());
    Assert.Empty(await StoredEnvelopeIdsAsync());
    Assert.Empty(failed.Sent.AllMessages());

    var relayed = 0;
    var retried = await TrackAsync(async () => relayed = await drain.DrainBatchAsync(default));

    Assert.Equal(2, relayed);
    Assert.Equal(facts, retried.Sent.MessagesOf<SubscriptionCanceled>().ToArray());
    Assert.Equal(2, (await StoredEnvelopeIdsAsync()).Count);
    Assert.Equal(0, await OutboxCountAsync());
  }

  [Fact]
  public async Task Rows_of_an_unknown_type_stay_in_the_table_and_do_not_hold_up_the_others()
  {
    var unknownFirst = await EnqueueAsync("PlanRetired", """{"planId":"gold"}""");
    var known = new SubscriptionCanceled(Guid.NewGuid(), new DateOnly(2026, 1, 15));
    await EnqueueAsync(known);
    var unknownLast = await EnqueueAsync("PlanRetired", """{"planId":"silver"}""");

    // A batch of one: an unknown row at the head must not use up the batch.
    var drain = fixture.CreateDrain(1);
    var relayed = 0;
    var session = await TrackAsync(async () => relayed = await drain.DrainBatchAsync(default));

    Assert.Equal(1, relayed);
    Assert.Equal(known, Assert.Single(session.Sent.AllMessages()));
    Assert.Equal([unknownFirst, unknownLast], await OutboxIdsAsync());

    var again = await TrackAsync(async () => relayed = await drain.DrainBatchAsync(default));
    Assert.Equal(0, relayed);
    Assert.Empty(again.Sent.AllMessages());
    Assert.Equal([unknownFirst, unknownLast], await OutboxIdsAsync());
  }

  [Fact]
  public async Task Unknown_types_warn_at_most_once_a_minute_and_remain_in_the_outbox()
  {
    var id = await EnqueueAsync("PlanRetired", """{"planId":"gold"}""");
    var logger = new WarningLog();
    var time = new TestTime();
    var drain = fixture.CreateDrain(logger: logger, time: time);

    Assert.Equal(0, await drain.DrainBatchAsync(default));
    Assert.Contains("1 rows", Assert.Single(logger.Warnings));
    Assert.Contains("PlanRetired", logger.Warnings[0]);

    time.Now = time.Now.AddSeconds(59);
    Assert.Equal(0, await drain.DrainBatchAsync(default));
    Assert.Single(logger.Warnings);

    time.Now = time.Now.AddSeconds(1);
    Assert.Equal(0, await drain.DrainBatchAsync(default));
    Assert.Equal(2, logger.Warnings.Count);
    Assert.Equal([id], await OutboxIdsAsync());
  }

  [Fact]
  public async Task The_relay_span_continues_the_trace_of_the_row_and_becomes_the_parent_of_the_envelope()
  {
    var traceId = ActivityTraceId.CreateRandom();
    var spanId = ActivitySpanId.CreateRandom();
    var traced = new InvoiceIssued(
      Guid.NewGuid(),
      Guid.NewGuid(),
      10m,
      "USD",
      new DateOnly(2026, 1, 1),
      new DateOnly(2026, 2, 1)
    );
    var untraced = new SubscriptionCanceled(Guid.NewGuid(), new DateOnly(2026, 1, 15));
    var tracedRow = await EnqueueAsync(traced, $"00-{traceId}-{spanId}-01");
    var untracedRow = await EnqueueAsync(untraced);

    var spans = new List<Activity>();
    using var listener = new ActivityListener
    {
      ShouldListenTo = source => source.Name == ServiceSetup.ActivitySourceName,
      Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
      ActivityStopped = spans.Add
    };
    ActivitySource.AddActivityListener(listener);

    var drain = fixture.CreateDrain();
    var session = await TrackAsync(() => drain.DrainBatchAsync(default));

    Assert.Equal(2, spans.Count);
    var tracedSpan = spans[0];
    Assert.Equal("relay InvoiceIssued", tracedSpan.OperationName);
    Assert.Equal(ActivityKind.Internal, tracedSpan.Kind);
    Assert.Equal(traceId, tracedSpan.TraceId);
    Assert.Equal(spanId, tracedSpan.ParentSpanId);
    Assert.Equal(tracedRow, tracedSpan.GetTagItem("outbox.message.id"));
    // Wolverine sends the envelope's ParentId as the "parent-id" header; the consumer's span hangs under it.
    var tracedEnvelope = session.Sent.SingleEnvelope<InvoiceIssued>();
    Assert.Equal(tracedSpan.Id, tracedEnvelope.ParentId);
    Assert.Equal(traceId.ToString(), tracedEnvelope.CorrelationId);

    // A row written without a trace starts one of its own instead of joining its neighbour's, in the same batch.
    var untracedSpan = spans[1];
    Assert.Equal("relay SubscriptionCanceled", untracedSpan.OperationName);
    Assert.NotEqual(traceId, untracedSpan.TraceId);
    Assert.Equal(default, untracedSpan.ParentSpanId);
    Assert.Equal(untracedRow, untracedSpan.GetTagItem("outbox.message.id"));
    var untracedEnvelope = session.Sent.SingleEnvelope<SubscriptionCanceled>();
    Assert.Equal(untracedSpan.Id, untracedEnvelope.ParentId);
    Assert.Equal(untracedSpan.TraceId.ToString(), untracedEnvelope.CorrelationId);
  }

  [Fact]
  public async Task Two_drains_running_at_once_publish_every_row_exactly_once()
  {
    var facts = Enumerable.Range(0, 30)
      .Select(_ => new SubscriptionCanceled(Guid.NewGuid(), new DateOnly(2026, 1, 15)))
      .ToArray();
    foreach (var fact in facts) await EnqueueAsync(fact);

    var first = fixture.CreateDrain(4);
    var second = fixture.CreateDrain(4);
    var relayed = Array.Empty<int>();
    var session = await TrackAsync(
      async () => relayed = await Task.WhenAll(DrainAllAsync(first), DrainAllAsync(second))
    );

    Assert.Equal(30, relayed.Sum());
    var published = session.Sent.MessagesOf<SubscriptionCanceled>().Select(m => m.SubscriptionId).ToArray();
    Assert.Equal(30, published.Length);
    Assert.Equal(facts.Select(f => f.SubscriptionId).Order(), published.Order());
    Assert.Equal(0, await OutboxCountAsync());

    // One drain can find every remaining row locked by the other and stop early; the other then finishes the table.
    static async Task<int> DrainAllAsync(
      OutboxDrain drain
    )
    {
      var total = 0;
      for (var relayed = -1; relayed != 0; total += relayed) relayed = await drain.DrainBatchAsync(default);

      return total;
    }
  }

  /// <summary>
  /// The background loop: with an hour between polls, five rows in batches of two are all relayed only if a full
  /// batch is followed by the next one straight away.
  /// </summary>
  [Fact]
  public async Task The_loop_relays_full_batches_back_to_back()
  {
    var facts = Enumerable.Range(1, 5)
      .Select(day => new SubscriptionCanceled(Guid.NewGuid(), new DateOnly(2026, 1, day)))
      .ToArray();
    foreach (var fact in facts) await EnqueueAsync(fact);

    var drain = fixture.CreateDrain(2, 3_600_000);
    ITrackedSession session;
    try
    {
      session = await fixture.App.TrackActivity(TimeSpan.FromSeconds(30))
        .WaitForCondition(new Published(facts.Length))
        .ExecuteAndWaitAsync(_ => drain.StartAsync(default));
    }
    finally
    {
      await drain.StopAsync(default);
    }

    Assert.Equal(facts, session.Sent.MessagesOf<SubscriptionCanceled>().ToArray());
    Assert.Equal(0, await OutboxCountAsync());
  }

  private Task<ITrackedSession> TrackAsync(
    Func<Task> action
  )
  {
    return fixture.App.TrackActivity(TimeSpan.FromSeconds(30)).ExecuteAndWaitAsync(_ => action());
  }

  private Task<long> EnqueueAsync(
    object message,
    string? traceparent = null
  )
  {
    return EnqueueAsync(
      message.GetType().Name,
      JsonSerializer.Serialize(message, message.GetType(), Web),
      traceparent
    );
  }

  private async Task<long> EnqueueAsync(
    string type,
    string payload,
    string? traceparent = null
  )
  {
    await using var connection = await fixture.OpenDbAsync();
    await using var command = new NpgsqlCommand(
      """
      INSERT INTO outbox_messages (type, payload, traceparent, occurred_at)
      VALUES ($1, $2, $3, now() AT TIME ZONE 'UTC')
      RETURNING id
      """,
      connection
    );
    command.Parameters.AddWithValue(type);
    command.Parameters.AddWithValue(payload);
    command.Parameters.Add(new NpgsqlParameter<string?> { TypedValue = traceparent });
    return (long)(await command.ExecuteScalarAsync())!;
  }

  private Task<long> OutboxCountAsync()
  {
    return fixture.ScalarAsync<long>("SELECT count(*) FROM outbox_messages");
  }

  private Task<long[]> OutboxIdsAsync()
  {
    return fixture.ScalarAsync<long[]>("SELECT coalesce(array_agg(id ORDER BY id), '{}') FROM outbox_messages");
  }

  private async Task<IReadOnlyList<Guid>> StoredEnvelopeIdsAsync()
  {
    return await fixture.ScalarAsync<Guid[]>("SELECT coalesce(array_agg(id), '{}') FROM stored_envelopes");
  }

  private sealed class TestTime : TimeProvider
  {
    public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
      return Now;
    }
  }

  private sealed class WarningLog : ILogger<OutboxDrain>
  {
    public List<string> Warnings { get; } = [];

    public IDisposable? BeginScope<TState>(
      TState state
    ) where TState : notnull
    {
      return null;
    }

    public bool IsEnabled(
      LogLevel logLevel
    )
    {
      return true;
    }

    public void Log<TState>(
      LogLevel logLevel,
      EventId eventId,
      TState state,
      Exception? exception,
      Func<TState, Exception?, string> formatter
    )
    {
      if (logLevel == LogLevel.Warning) Warnings.Add(formatter(state, exception));
    }
  }

  /// <summary>Completes a tracked session once that many messages have been handed to the transport.</summary>
  private sealed class Published(
    int expected
  ) : ITrackedCondition
  {
    private int _sent;

    public void Record(
      EnvelopeRecord record
    )
    {
      if (record.MessageEventType == MessageEventType.Sent) Interlocked.Increment(ref _sent);
    }

    public bool IsCompleted()
    {
      return Volatile.Read(ref _sent) >= expected;
    }
  }
}