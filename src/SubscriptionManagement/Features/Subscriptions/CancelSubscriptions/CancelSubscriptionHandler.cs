using Npgsql;
using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.Features.Subscriptions.CancelSubscriptions;

public sealed record CancelSubscriptionCommand(Guid Id);

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
        CancellationToken ct)
    {
        await db.InTransactionAsync(async (connection, token) =>
        {
            var subscription = await SubscriptionStore.FindForUpdateAsync(connection, command.Id, token)
                ?? throw SubscriptionProblems.NotFound(command.Id);
            subscription.Cancel();
            await SubscriptionStore.SaveStateAsync(connection, subscription, token);
            return true;
        }, ct);

        await cache.EvictAsync(command.Id, CancellationToken.None); // after commit; the next read reloads
    }
}
