using SubscriptionManagement.Infrastructure;
using Npgsql;

namespace SubscriptionManagement.Features.Plans.ListPlans;

public static class ListPlansHandler
{
    public static async Task<PlanList> Handle(NpgsqlDataSource db, CancellationToken ct)
    {
        var plans = await db.WithConnectionAsync((connection, token) => PlanStore.ListActiveAsync(connection, token), ct);
        return new PlanList(plans.Select(PlanResponse.From).ToList());
    }
}

public static class ListPlansEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/plans", async (NpgsqlDataSource db, CancellationToken ct) =>
            TypedResults.Ok(await ListPlansHandler.Handle(db, ct)));
}
