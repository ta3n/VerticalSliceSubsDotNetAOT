using System.Text.Json;
using Npgsql;
using SubscriptionManagement.Contracts;
using SubscriptionManagement.Features.Invoices;
using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.Features.Subscriptions.CancelSubscriptions;

public sealed record CancelSubscriptionCommand(
  Guid Id
);

public static class CancelSubscriptionHandler
{
  /// <summary>
  /// Locks the row (<c>FOR UPDATE</c>) before checking the status so overlapping cancellations of the same
  /// subscription run one after another: the first succeeds, the rest see "already canceled".
  /// </summary>
  public static async Task Handle(
    CancelSubscriptionCommand command,
    NpgsqlDataSource db,
    SubscriptionCache cache,
    TimeProvider time,
    OutboxOptions outbox,
    CancellationToken ct
  )
  {
    await db.InTransactionAsync(
      async (connection, token) =>
      {
        var subscription = await SubscriptionStore.FindForUpdateAsync(connection, command.Id, token)
          ?? throw SubscriptionProblems.NotFound(command.Id);
        subscription.Cancel();
        await SubscriptionStore.SaveStateAsync(connection, subscription, token);

        // Payments refunds what it collected for the unused period. Only a subscription with a collected
        // payment has anything to refund, so every other cancellation stays off the broker.
        if (outbox.Enabled && await InvoiceStore.HasCollectedPaymentAsync(connection, subscription.Id, token))
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

        return true;
      },
      ct
    );

    await cache.EvictAsync(command.Id, CancellationToken.None); // after commit; the next read reloads
  }
}