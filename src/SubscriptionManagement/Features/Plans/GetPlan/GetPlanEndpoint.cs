using SubscriptionManagement.Infrastructure;
using Npgsql;

namespace SubscriptionManagement.Features.Plans.GetPlan;

public sealed record GetPlanQuery(string Code);

public static class GetPlanHandler
{
    public static async Task<PlanResponse> Handle(GetPlanQuery query, NpgsqlDataSource db, CancellationToken ct)
    {
        var plan = await db.WithConnectionAsync((connection, token) => PlanStore.FindActiveAsync(connection, query.Code, token), ct)
            ?? throw PlanProblems.NotFound(query.Code);
        return PlanResponse.From(plan);
    }
}

public static class GetPlanEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/plans/{code}", async (string code, NpgsqlDataSource db, CancellationToken ct) =>
            TypedResults.Ok(await GetPlanHandler.Handle(new GetPlanQuery(code), db, ct)));
}
