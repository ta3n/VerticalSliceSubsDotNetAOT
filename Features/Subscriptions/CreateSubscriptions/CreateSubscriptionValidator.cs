using FluentValidation;

namespace SubscriptionManagement.Features.Subscriptions.CreateSubscriptions;

public class CreateSubscriptionValidator : AbstractValidator<CreateSubscriptionCommand>
{
    public CreateSubscriptionValidator()
    {
        RuleFor(x => x.ServiceName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.Price)
            .GreaterThan(0);

        RuleFor(x => x.TrialEndsAt)
            .GreaterThan(x => x.StartDate)
            .When(x => x.TrialEndsAt.HasValue);
    }
}
