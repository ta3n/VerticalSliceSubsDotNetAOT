using Npgsql;

namespace SubscriptionManagement.Features.Plans.CreatePlan;

public static class CreatePlanEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/plans", async (CreatePlanCommand command, NpgsqlDataSource db, CancellationToken ct) =>
        {
            var response = await CreatePlanHandler.Handle(command, db, ct);
            return TypedResults.Created($"/plans/{response.Code}", response);
        });
}
