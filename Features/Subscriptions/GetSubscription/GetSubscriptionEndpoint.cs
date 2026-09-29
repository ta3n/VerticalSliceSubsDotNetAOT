using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.Features.Subscriptions.GetSubscription;

public static class GetSubscriptionEndpoint
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/subscriptions/{id:guid}",
            async (Guid id, AppDbContext dbContext) =>
            {
                var query = new GetSubscriptionQuery(id);
                return await GetSubscriptionHandler.Handle(query, dbContext);
            });
    }
}