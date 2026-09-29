using Npgsql;

namespace SubscriptionManagement.Features.Subscriptions.PauseSubscription;

public static class PauseSubscriptionEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/subscriptions/{id}/pause",
            async (Guid id, NpgsqlDataSource db, SubscriptionCache cache, CancellationToken ct) =>
                TypedResults.Ok(await PauseSubscriptionHandler.Handle(new PauseSubscriptionCommand(id), db, cache, ct)));
}
