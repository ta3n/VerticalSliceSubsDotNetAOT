using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.Features.Subscriptions.CancelSubscriptions;

public static class CancelSubscriptionEndpoint
{
    public static void Map(WebApplication app)
    {
        app.MapDelete("/subscriptions/{id:guid}",
            async (Guid id, AppDbContext dbContext) =>
            {
                var command = new CancelSubscriptionCommand(id);
                return await CancelSubscriptionHandler.Handle(command, dbContext);
            });
    }
}