using Npgsql;

namespace SubscriptionManagement.Features.Subscriptions.ResumeSubscription;

public static class ResumeSubscriptionEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/subscriptions/{id}/resume",
            async (Guid id, NpgsqlDataSource db, SubscriptionCache cache, CancellationToken ct) =>
                TypedResults.Ok(await ResumeSubscriptionHandler.Handle(new ResumeSubscriptionCommand(id), db, cache, ct)));
}
