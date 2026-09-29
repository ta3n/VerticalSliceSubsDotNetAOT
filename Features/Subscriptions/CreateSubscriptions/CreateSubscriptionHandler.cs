using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.Features.Subscriptions.CreateSubscriptions;

public static class CreateSubscriptionHandler
{
    public static async Task<IResult> Handle(CreateSubscriptionCommand command, AppDbContext dbContext)
    {
        var subscription = new Subscription(
            command.ServiceName,
            command.Price,
            command.StartDate,
            command.TrialEndsAt
        );

        dbContext.Subscriptions.Add(subscription);
        await dbContext.SaveChangesAsync();

        return Results.Created($"/subscriptions/{subscription.Id}", subscription);
    }
}