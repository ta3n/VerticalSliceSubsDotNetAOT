using FluentValidation;
using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.Features.Subscriptions.CreateSubscriptions;

public class CreateSubscriptionEndpoint
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/subscriptions",
            async (CreateSubscriptionCommand command,
                AppDbContext dbContext,
                IValidator<CreateSubscriptionCommand> validator) =>
            {
                var validation = await validator.ValidateAsync(command);

                if (!validation.IsValid)
                {
                    return Results.ValidationProblem(validation.ToDictionary());
                }

                return await CreateSubscriptionHandler.Handle(command, dbContext);
            });
    }
}