using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SubscriptionManagement.Billing.Features.Renewals;
using SubscriptionManagement.Billing.Infrastructure;
using SubscriptionManagement.Contracts;
using Wolverine;

namespace SubscriptionManagement.Billing.Tests;

/// <summary>
/// The saga driven through Wolverine against real PostgreSQL: what is stored, what is sent, and that both happen in
/// one transaction. No broker: messages bound for RabbitMQ are captured by the tracked session.
/// </summary>
[Collection(BillingCollection.Name)]
public class RenewalSagaFlowTests(
  BillingFixture fixture
) : IAsyncLifetime
{
  private const string ChargePaymentType = "SubscriptionManagement.Contracts.ChargePayment";
  private const string RenewalTimeoutType = "SubscriptionManagement.Billing.Features.Renewals.RenewalTimeout";

  /// <summary>
  /// Triggers that note the transaction id of every insert into the saga table and Wolverine's two envelope tables.
  /// Rows written by one transaction share a <c>txid</c>; a rolled-back transaction leaves no rows at all.
  /// </summary>
  public async Task InitializeAsync()
  {
    await ExecuteAsync(
      """
      CREATE TABLE IF NOT EXISTS write_audit (
          source TEXT NOT NULL, txid BIGINT NOT NULL, row_id TEXT NOT NULL, message_type TEXT NULL, status TEXT NULL);
      CREATE OR REPLACE FUNCTION audit_insert() RETURNS trigger AS $$
      BEGIN
          INSERT INTO write_audit (source, txid, row_id, message_type, status)
          VALUES (TG_TABLE_NAME, txid_current(), to_jsonb(NEW)->>'id', to_jsonb(NEW)->>'message_type', to_jsonb(NEW)->>'status');
          RETURN NULL;
      END $$ LANGUAGE plpgsql;
      CREATE OR REPLACE TRIGGER audit_renewal_sagas AFTER INSERT ON renewal_sagas
          FOR EACH ROW EXECUTE FUNCTION audit_insert();
      CREATE OR REPLACE TRIGGER audit_outgoing AFTER INSERT ON wolverine.wolverine_outgoing_envelopes
          FOR EACH ROW EXECUTE FUNCTION audit_insert();
      CREATE OR REPLACE TRIGGER audit_incoming AFTER INSERT ON wolverine.wolverine_incoming_envelopes
          FOR EACH ROW EXECUTE FUNCTION audit_insert();
      """
    );
  }

  public Task DisposeAsync()
  {
    return Task.CompletedTask;
  }

  [Fact]
  public async Task InvoiceIssued_commits_the_saga_the_charge_and_the_timeout_in_one_transaction()
  {
    var issued = NewInvoice(11m);

    var before = DateTimeOffset.UtcNow;
    var session = await fixture.Track().InvokeMessageAndWaitAsync(issued);
    var after = DateTimeOffset.UtcNow;

    var saga = await FindAsync(issued.InvoiceId);
    Assert.NotNull(saga);
    Assert.Equal(RenewalState.AwaitingPayment, saga.State);
    Assert.Equal(issued.SubscriptionId, saga.SubscriptionId);
    Assert.Equal(11m, saga.Amount);
    Assert.Equal("USD", saga.Currency);
    Assert.Null(saga.FailureReason);
    Assert.InRange(saga.UpdatedAt, before.AddMilliseconds(-1), after);

    // Bound for the broker, to the exchange named after the type; the timeout stays on a local queue.
    var charge = session.Sent.SingleEnvelope<ChargePayment>();
    Assert.Equal(
      new ChargePayment(issued.InvoiceId, saga.PaymentId, issued.SubscriptionId, 11m, "USD", issued.PeriodEnd),
      charge.Message
    );
    Assert.Equal(new Uri($"rabbitmq://exchange/{ChargePaymentType}"), charge.Destination);

    var timeout = session.Sent.SingleEnvelope<RenewalTimeout>();
    Assert.Equal(new RenewalTimeout(issued.InvoiceId), timeout.Message);
    Assert.Equal("local", timeout.Destination!.Scheme);
    var delay = TimeSpan.FromSeconds(BillingFixture.RenewalTimeoutSeconds);
    Assert.InRange(timeout.ScheduledTime!.Value, before + delay, after + delay);

    // One transaction wrote the saga row, the outbox row of the charge and the scheduled timeout.
    var writes = await AuditedWritesInTransactionOfAsync(issued.InvoiceId);
    Assert.Equal(3, writes.Count);
    Assert.Contains(("renewal_sagas", null, null), writes);
    Assert.Contains(("wolverine_outgoing_envelopes", ChargePaymentType, null), writes);
    Assert.Contains(("wolverine_incoming_envelopes", RenewalTimeoutType, "Scheduled"), writes);

    // The timeout is durable: it is still stored, due in the future, after the handler has finished.
    Assert.Equal(1, await CountScheduledTimeoutsAsync(timeout.Id));
  }

  [Fact]
  public async Task When_the_saga_cannot_be_stored_nothing_is_sent_or_scheduled()
  {
    var issued = NewInvoice();
    await fixture.Track().InvokeMessageAndWaitAsync(issued);
    var stored = await FindAsync(issued.InvoiceId);
    var auditedBefore = await CountAuditedWritesAsync();

    // A second saga for the same invoice violates the primary key when the transaction is committed.
    Exception? failure = null;
    var session = await fixture.Track()
      .DoNotAssertOnExceptionsDetected()
      .ExecuteAndWaitAsync(
        async Task (context) =>
        {
          try
          {
            await context.InvokeAsync(issued);
          }
          catch (Exception e)
          {
            failure = e;
          }
        }
      );

    var postgres = Assert.IsType<PostgresException>(Assert.IsType<DbUpdateException>(failure).InnerException);
    Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
    Assert.Empty(session.Sent.AllMessages());
    Assert.Equal(auditedBefore, await CountAuditedWritesAsync());

    var unchanged = await FindAsync(issued.InvoiceId);
    Assert.Equal(stored!.PaymentId, unchanged!.PaymentId);
    Assert.Equal(0, unchanged.Version);
  }

  [Fact]
  public async Task Paid_and_settled_renewal_ends_Completed_and_the_row_is_kept()
  {
    var issued = NewInvoice();
    var started = await fixture.Track().InvokeMessageAndWaitAsync(issued);
    var paymentId = started.Sent.SingleMessage<ChargePayment>().PaymentId;

    var paid = await fixture.Track().InvokeMessageAndWaitAsync(new PaymentSucceeded(issued.InvoiceId, paymentId));

    Assert.Equal(new SettleInvoice(issued.InvoiceId, paymentId), Assert.Single(paid.Sent.AllMessages()));
    Assert.Equal(RenewalState.Settling, (await FindAsync(issued.InvoiceId))!.State);

    var settled = await fixture.Track().InvokeMessageAndWaitAsync(new InvoiceSettled(issued.InvoiceId));

    Assert.Empty(settled.Sent.AllMessages());
    var saga = await FindAsync(issued.InvoiceId);
    Assert.NotNull(saga);
    Assert.Equal(RenewalState.Completed, saga.State);
    Assert.Null(saga.FailureReason);
    // Start stores version 0; each of the two transitions increments it.
    Assert.Equal(2, saga.Version);
  }

  [Fact]
  public async Task Declined_payment_cancels_the_renewal()
  {
    var issued = NewInvoice();
    var started = await fixture.Track().InvokeMessageAndWaitAsync(issued);
    var paymentId = started.Sent.SingleMessage<ChargePayment>().PaymentId;

    var declined = await fixture.Track()
      .InvokeMessageAndWaitAsync(
        new PaymentFailed(issued.InvoiceId, paymentId, "insufficient funds")
      );

    Assert.Equal(
      new CancelRenewal(issued.InvoiceId, issued.SubscriptionId, "insufficient funds"),
      Assert.Single(declined.Sent.AllMessages())
    );
    var saga = await FindAsync(issued.InvoiceId);
    Assert.Equal(RenewalState.Declined, saga!.State);
    Assert.Equal("insufficient funds", saga.FailureReason);
  }

  [Fact]
  public async Task The_scheduled_timeout_cancels_both_sides_and_a_late_reply_changes_nothing()
  {
    var issued = NewInvoice();
    var started = await fixture.Track().InvokeMessageAndWaitAsync(issued);
    var paymentId = started.Sent.SingleMessage<ChargePayment>().PaymentId;

    // Deliver the very message the saga scheduled, instead of waiting for its due time.
    var timedOut = await fixture.Track().InvokeMessageAndWaitAsync(started.Sent.SingleMessage<RenewalTimeout>());

    Assert.Equal(
      [
        new CancelPayment(paymentId, RenewalSaga.TimeoutReason),
        new CancelRenewal(issued.InvoiceId, issued.SubscriptionId, RenewalSaga.TimeoutReason)
      ],
      timedOut.Sent.AllMessages()
    );
    var saga = await FindAsync(issued.InvoiceId);
    Assert.Equal(RenewalState.TimedOut, saga!.State);
    Assert.Equal(RenewalSaga.TimeoutReason, saga.FailureReason);

    var late = await fixture.Track().InvokeMessageAndWaitAsync(new PaymentSucceeded(issued.InvoiceId, paymentId));

    Assert.Empty(late.Sent.AllMessages());
    var unchanged = await FindAsync(issued.InvoiceId);
    Assert.Equal(RenewalState.TimedOut, unchanged!.State);
    Assert.Equal(saga.Version, unchanged.Version);
    Assert.Equal(saga.UpdatedAt, unchanged.UpdatedAt);
  }

  [Fact]
  public async Task Timeout_after_the_payment_is_confirmed_does_not_cancel_anything()
  {
    var issued = NewInvoice();
    var started = await fixture.Track().InvokeMessageAndWaitAsync(issued);
    var paymentId = started.Sent.SingleMessage<ChargePayment>().PaymentId;
    await fixture.Track().InvokeMessageAndWaitAsync(new PaymentSucceeded(issued.InvoiceId, paymentId));

    var timeout = await fixture.Track().InvokeMessageAndWaitAsync(new RenewalTimeout(issued.InvoiceId));

    Assert.Empty(timeout.Sent.AllMessages());
    var saga = await FindAsync(issued.InvoiceId);
    Assert.Equal(RenewalState.Settling, saga!.State);
    Assert.Equal(1, saga.Version);
  }

  [Fact]
  public async Task Rejected_settlement_refunds_the_payment()
  {
    var issued = NewInvoice();
    var started = await fixture.Track().InvokeMessageAndWaitAsync(issued);
    var paymentId = started.Sent.SingleMessage<ChargePayment>().PaymentId;
    await fixture.Track().InvokeMessageAndWaitAsync(new PaymentSucceeded(issued.InvoiceId, paymentId));

    var rejected = await fixture.Track()
      .InvokeMessageAndWaitAsync(
        new InvoiceSettlementRejected(issued.InvoiceId, "invoice-not-payable")
      );

    Assert.Equal(new CancelPayment(paymentId, "invoice-not-payable"), Assert.Single(rejected.Sent.AllMessages()));
    var saga = await FindAsync(issued.InvoiceId);
    Assert.Equal(RenewalState.Compensated, saga!.State);
    Assert.Equal("invoice-not-payable", saga.FailureReason);
  }

  public static TheoryData<string> RepliesAndTimeout()
  {
    return
    [
      nameof(PaymentSucceeded), nameof(PaymentFailed), nameof(RenewalTimeout), nameof(InvoiceSettled),
      nameof(InvoiceSettlementRejected)
    ];
  }

  [Theory]
  [MemberData(nameof(RepliesAndTimeout))]
  public async Task A_message_for_an_invoice_without_a_saga_is_dropped(
    string message
  )
  {
    var unknown = Guid.NewGuid();
    object reply = message switch
    {
      nameof(PaymentSucceeded) => new PaymentSucceeded(unknown, Guid.NewGuid()),
      nameof(PaymentFailed) => new PaymentFailed(unknown, Guid.NewGuid(), "declined"),
      nameof(RenewalTimeout) => new RenewalTimeout(unknown),
      nameof(InvoiceSettled) => new InvoiceSettled(unknown),
      _ => new InvoiceSettlementRejected(unknown, "rejected")
    };

    // The session fails the test if the handler throws.
    var session = await fixture.Track().InvokeMessageAndWaitAsync(reply);

    Assert.Empty(session.Sent.AllMessages());
    Assert.Null(await FindAsync(unknown));
  }

  [Fact]
  public async Task A_write_based_on_a_stale_version_is_rejected()
  {
    var issued = NewInvoice();
    await fixture.Track().InvokeMessageAndWaitAsync(issued);

    await using var replyScope = fixture.App.Services.CreateAsyncScope();
    await using var timeoutScope = fixture.App.Services.CreateAsyncScope();
    var replyDb = replyScope.ServiceProvider.GetRequiredService<BillingDbContext>();
    var timeoutDb = timeoutScope.ServiceProvider.GetRequiredService<BillingDbContext>();

    // Both read version 0, as two handlers running at the same moment would.
    var seenByReply = await replyDb.RenewalSagas.SingleAsync(x => x.Id == issued.InvoiceId);
    var seenByTimeout = await timeoutDb.RenewalSagas.SingleAsync(x => x.Id == issued.InvoiceId);

    seenByReply.State = RenewalState.Settling;
    seenByReply.Version++;
    await replyDb.SaveChangesAsync();

    seenByTimeout.State = RenewalState.TimedOut;
    seenByTimeout.Version++;
    await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => timeoutDb.SaveChangesAsync());

    var saga = await FindAsync(issued.InvoiceId);
    Assert.Equal(RenewalState.Settling, saga!.State);
    Assert.Equal(1, saga.Version);
  }

  [Fact]
  public async Task Of_a_timeout_racing_the_payment_reply_exactly_one_takes_effect()
  {
    const int renewals = 12;
    var started = new List<(InvoiceIssued Issued, Guid PaymentId)>();
    for (var i = 0; i < renewals; i++)
    {
      var issued = NewInvoice();
      var session = await fixture.Track().InvokeMessageAndWaitAsync(issued);
      started.Add((issued, session.Sent.SingleMessage<ChargePayment>().PaymentId));
    }

    // Reply and timeout of every renewal are handled concurrently, each on its own message bus. The loser of a
    // collision fails on the version check, is retried, finds the new state and is ignored.
    var failures = new List<Exception>();
    var race = await fixture.Track()
      .DoNotAssertOnExceptionsDetected()
      .ExecuteAndWaitAsync(
        _ =>
          Task.WhenAll(
            started.SelectMany(
                x => new object[]
                {
                  new PaymentSucceeded(x.Issued.InvoiceId, x.PaymentId),
                  new RenewalTimeout(x.Issued.InvoiceId)
                }
              )
              .Select(
                message => Task.Run(
                  async () =>
                  {
                    try
                    {
                      await using var scope = fixture.App.Services.CreateAsyncScope();
                      await scope.ServiceProvider.GetRequiredService<IMessageBus>()
                        .InvokeAsync(message);
                    }
                    catch (Exception e)
                    {
                      lock (failures)
                      {
                        failures.Add(e);
                      }
                    }
                  }
                )
              )
          )
      );

    Assert.Empty(failures);

    var sent = race.Sent.AllMessages().ToList();
    foreach (var (issued, paymentId) in started)
    {
      var settle = sent.OfType<SettleInvoice>().Where(x => x.RenewalSagaId == issued.InvoiceId).ToList();
      var cancelPayment = sent.OfType<CancelPayment>().Where(x => x.PaymentId == paymentId).ToList();
      var cancelRenewal = sent.OfType<CancelRenewal>().Where(x => x.InvoiceId == issued.InvoiceId).ToList();

      var saga = await FindAsync(issued.InvoiceId);
      Assert.Equal(1, saga!.Version);
      if (saga.State == RenewalState.Settling)
      {
        Assert.Single(settle);
        Assert.Empty(cancelPayment);
        Assert.Empty(cancelRenewal);
      }
      else
      {
        Assert.Equal(RenewalState.TimedOut, saga.State);
        Assert.Empty(settle);
        Assert.Single(cancelPayment);
        Assert.Single(cancelRenewal);
      }
    }
  }

  [Fact]
  public async Task A_transient_database_failure_is_retried_and_the_transition_happens_once()
  {
    var issued = NewInvoice();
    var started = await fixture.Track().InvokeMessageAndWaitAsync(issued);
    var paymentId = started.Sent.SingleMessage<ChargePayment>().PaymentId;

    // The first two updates of this saga fail with a serialization failure (40001), which PostgreSQL asks the
    // client to retry. A sequence counts the attempts because, unlike a row, it is not rolled back with them.
    await ExecuteAsync(
      $"""
      CREATE SEQUENCE IF NOT EXISTS failed_updates;
      CREATE OR REPLACE FUNCTION fail_twice() RETURNS trigger AS $$
      BEGIN
          IF nextval('failed_updates') <= 2 THEN
              RAISE EXCEPTION 'simulated serialization failure' USING ERRCODE = '40001';
          END IF;
          RETURN NEW;
      END $$ LANGUAGE plpgsql;
      CREATE OR REPLACE TRIGGER fail_twice BEFORE UPDATE ON renewal_sagas
          FOR EACH ROW WHEN (OLD.id = '{issued.InvoiceId}') EXECUTE FUNCTION fail_twice();
      """
    );
    try
    {
      var paid = await fixture.Track()
        .DoNotAssertOnExceptionsDetected()
        .InvokeMessageAndWaitAsync(new PaymentSucceeded(issued.InvoiceId, paymentId));

      Assert.Equal(new SettleInvoice(issued.InvoiceId, paymentId), Assert.Single(paid.Sent.AllMessages()));
      var saga = await FindAsync(issued.InvoiceId);
      Assert.Equal(RenewalState.Settling, saga!.State);
      Assert.Equal(1, saga.Version);
      Assert.Equal(3L, await ScalarAsync("SELECT last_value FROM failed_updates"));
    }
    finally
    {
      await ExecuteAsync("DROP TRIGGER fail_twice ON renewal_sagas; DROP SEQUENCE failed_updates;");
    }
  }

  private static InvoiceIssued NewInvoice(
    decimal amount = 11m
  )
  {
    return new InvoiceIssued(
      Guid.NewGuid(),
      Guid.NewGuid(),
      amount,
      "USD",
      new DateOnly(2026, 9, 12),
      new DateOnly(2026, 10, 12)
    );
  }

  private Task<RenewalSaga?> FindAsync(
    Guid invoiceId
  )
  {
    return fixture.QueryAsync(db => db.RenewalSagas.AsNoTracking().SingleOrDefaultAsync(x => x.Id == invoiceId));
  }

  private async Task<List<(string Source, string? MessageType, string? Status)>> AuditedWritesInTransactionOfAsync(
    Guid sagaId
  )
  {
    await using var connection = await fixture.OpenDbAsync();
    await using var command = new NpgsqlCommand(
      """
      SELECT source, message_type, status FROM write_audit
      WHERE txid = (SELECT txid FROM write_audit WHERE source = 'renewal_sagas' AND row_id = $1)
      """,
      connection
    );
    command.Parameters.AddWithValue(sagaId.ToString());

    var writes = new List<(string, string?, string?)>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
      writes.Add(
        (
          reader.GetString(0),
          reader.IsDBNull(1) ? null : reader.GetString(1),
          reader.IsDBNull(2) ? null : reader.GetString(2))
      );

    return writes;
  }

  private Task<long> CountAuditedWritesAsync()
  {
    return ScalarAsync("SELECT count(*) FROM write_audit");
  }

  private async Task<long> ScalarAsync(
    string sql
  )
  {
    await using var connection = await fixture.OpenDbAsync();
    await using var command = new NpgsqlCommand(sql, connection);
    return (long)(await command.ExecuteScalarAsync())!;
  }

  private async Task ExecuteAsync(
    string sql
  )
  {
    await using var connection = await fixture.OpenDbAsync();
    await using var command = new NpgsqlCommand(sql, connection);
    await command.ExecuteNonQueryAsync();
  }

  private async Task<long> CountScheduledTimeoutsAsync(
    Guid envelopeId
  )
  {
    await using var connection = await fixture.OpenDbAsync();
    await using var command = new NpgsqlCommand(
      """
      SELECT count(*) FROM wolverine.wolverine_incoming_envelopes
      WHERE id = $1 AND status = 'Scheduled' AND message_type = $2 AND execution_time > now()
      """,
      connection
    );
    command.Parameters.AddWithValue(envelopeId);
    command.Parameters.AddWithValue(RenewalTimeoutType);
    return (long)(await command.ExecuteScalarAsync())!;
  }
}