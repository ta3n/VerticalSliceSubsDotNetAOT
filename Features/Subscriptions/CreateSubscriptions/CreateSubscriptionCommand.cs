namespace SubscriptionManagement.Features.Subscriptions.CreateSubscriptions;

public record CreateSubscriptionCommand(
    string ServiceName,
    decimal Price,
    DateOnly StartDate,
    DateOnly? TrialEndsAt
);