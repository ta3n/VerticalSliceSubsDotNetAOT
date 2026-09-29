using Microsoft.EntityFrameworkCore;
using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.Features.Subscriptions.GetSubscription;

public static class GetSubscriptionHandler
{
    public static async Task<IResult> Handle(
        GetSubscriptionQuery query,
        AppDbContext dbContext)
    {
        var subscription = await dbContext.Subscriptions
            .FirstOrDefaultAsync(x => x.Id == query.Id);

        if (subscription is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(subscription);
    }
}