using Microsoft.EntityFrameworkCore;
using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.Features.Subscriptions.CancelSubscriptions;

public static class CancelSubscriptionHandler
{
    public static async Task<IResult> Handle(
        CancelSubscriptionCommand command,
        AppDbContext dbContext)
    {
        var subscription = await dbContext.Subscriptions
            .FirstOrDefaultAsync(x => x.Id == command.Id);

        if (subscription is null)
        {
            return Results.NotFound();
        }

        try
        {
            subscription.Cancel();
            await dbContext.SaveChangesAsync();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

        return Results.NoContent();
    }
}