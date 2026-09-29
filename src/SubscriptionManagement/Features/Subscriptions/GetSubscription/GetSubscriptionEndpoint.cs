using Npgsql;

namespace SubscriptionManagement.Features.Subscriptions.GetSubscription;

public static class GetSubscriptionEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/subscriptions/{id}",
            async (Guid id, NpgsqlDataSource db, SubscriptionCache cache, CancellationToken ct) =>
                TypedResults.Ok(await GetSubscriptionHandler.Handle(new GetSubscriptionQuery(id), db, cache, ct)));
}
