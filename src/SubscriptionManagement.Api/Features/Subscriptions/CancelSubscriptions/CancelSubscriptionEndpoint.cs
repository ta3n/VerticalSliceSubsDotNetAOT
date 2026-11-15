using Npgsql;
using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.Features.Subscriptions.CancelSubscriptions;

public static class CancelSubscriptionEndpoint
{
    public static void Map(
        IEndpointRouteBuilder app
    )
    {
        app.MapDelete(
            "/subscriptions/{id}",
            async (
                Guid id, NpgsqlDataSource db, SubscriptionCache cache, TimeProvider time, OutboxOptions outbox,
                CancellationToken ct
            ) =>
            {
                await CancelSubscriptionHandler.Handle(new CancelSubscriptionCommand(id), db, cache, time, outbox, ct);
                return TypedResults.NoContent();
            }
        );
    }
}