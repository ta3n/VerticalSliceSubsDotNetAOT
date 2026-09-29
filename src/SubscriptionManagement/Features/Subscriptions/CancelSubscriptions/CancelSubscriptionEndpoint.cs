using Npgsql;

namespace SubscriptionManagement.Features.Subscriptions.CancelSubscriptions;

public static class CancelSubscriptionEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapDelete("/subscriptions/{id}",
            async (Guid id, NpgsqlDataSource db, SubscriptionCache cache, CancellationToken ct) =>
            {
                await CancelSubscriptionHandler.Handle(new CancelSubscriptionCommand(id), db, cache, ct);
                return TypedResults.NoContent();
            });
}
