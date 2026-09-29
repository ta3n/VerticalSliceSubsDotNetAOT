using Npgsql;

namespace SubscriptionManagement.Features.Subscriptions.CreateSubscriptions;

public static class CreateSubscriptionEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/subscriptions",
            async (CreateSubscriptionCommand command, NpgsqlDataSource db, SubscriptionCache cache, CancellationToken ct) =>
            {
                var response = await CreateSubscriptionHandler.Handle(command, db, cache, ct);
                return TypedResults.Created($"/subscriptions/{response.Id}", response);
            });
}
