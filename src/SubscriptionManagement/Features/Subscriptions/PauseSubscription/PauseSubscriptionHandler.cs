using Npgsql;
using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.Features.Subscriptions.PauseSubscription;

public sealed record PauseSubscriptionCommand(Guid Id);

public static class PauseSubscriptionHandler
{
    public static async Task<SubscriptionResponse> Handle(
        PauseSubscriptionCommand command,
        NpgsqlDataSource db,
        SubscriptionCache cache,
        CancellationToken ct)
    {
        var response = await db.InTransactionAsync(async (connection, token) =>
        {
            var subscription = await SubscriptionStore.FindForUpdateAsync(connection, command.Id, token)
                ?? throw SubscriptionProblems.NotFound(command.Id);
            subscription.Pause();
            await SubscriptionStore.SaveStateAsync(connection, subscription, token);
            return SubscriptionResponse.From(subscription);
        }, ct);

        await cache.PutAsync(response, CancellationToken.None); // refresh the cached view after commit
        return response;
    }
}
