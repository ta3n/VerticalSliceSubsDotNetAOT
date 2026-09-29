using Npgsql;

namespace SubscriptionManagement.Features.Subscriptions.SearchSubscriptions;

public static class SearchSubscriptionsEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/subscriptions/search",
            async (SearchSubscriptionsQuery? query, NpgsqlDataSource db, CancellationToken ct) =>
                TypedResults.Ok(await SearchSubscriptionsHandler.Handle(
                    query ?? new SearchSubscriptionsQuery(null, null, null), db, ct)));
}
