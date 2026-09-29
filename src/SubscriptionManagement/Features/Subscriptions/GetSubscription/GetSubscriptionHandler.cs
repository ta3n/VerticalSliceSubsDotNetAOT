using Npgsql;
using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.Features.Subscriptions.GetSubscription;

public sealed record GetSubscriptionQuery(Guid Id);

public static class GetSubscriptionHandler
{
    /// <summary>Served from the cache on a hit; a miss loads from the database and populates it. Not-found is never cached.</summary>
    public static async Task<SubscriptionResponse> Handle(
        GetSubscriptionQuery query,
        NpgsqlDataSource db,
        SubscriptionCache cache,
        CancellationToken ct)
    {
        if (await cache.GetAsync(query.Id, ct) is { } cached)
        {
            return cached;
        }

        var subscription = await db.WithConnectionAsync(
            (connection, token) => SubscriptionStore.FindAsync(connection, query.Id, token), ct)
            ?? throw SubscriptionProblems.NotFound(query.Id);

        var response = SubscriptionResponse.From(subscription);
        await cache.PutAsync(response, ct);
        return response;
    }
}
