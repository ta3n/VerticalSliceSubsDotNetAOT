using Npgsql;
using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.Features.Subscriptions.ResumeSubscription;

public sealed record ResumeSubscriptionCommand(Guid Id);

public static class ResumeSubscriptionHandler
{
    public static async Task<SubscriptionResponse> Handle(
        ResumeSubscriptionCommand command,
        NpgsqlDataSource db,
        SubscriptionCache cache,
        CancellationToken ct)
    {
        var response = await db.InTransactionAsync(async (connection, token) =>
        {
            var subscription = await SubscriptionStore.FindForUpdateAsync(connection, command.Id, token)
                ?? throw SubscriptionProblems.NotFound(command.Id);
            subscription.Resume();
            await SubscriptionStore.SaveStateAsync(connection, subscription, token);
            return SubscriptionResponse.From(subscription);
        }, ct);

        await cache.PutAsync(response, CancellationToken.None); // refresh the cached view after commit
        return response;
    }
}
