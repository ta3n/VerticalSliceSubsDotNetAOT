using Npgsql;

namespace SubscriptionManagement.Features.Subscriptions.RenewSubscription;

public static class RenewSubscriptionEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/subscriptions/{id}/renew",
            async (Guid id, NpgsqlDataSource db, SubscriptionCache cache, TimeProvider time, CancellationToken ct) =>
                TypedResults.Ok(await RenewSubscriptionHandler.Handle(new RenewSubscriptionCommand(id), db, cache, time, ct)));
}
