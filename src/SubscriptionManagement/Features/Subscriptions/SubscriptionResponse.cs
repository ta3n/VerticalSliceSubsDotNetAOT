namespace SubscriptionManagement.Features.Subscriptions;

/// <summary>Public contract: the original six fields, unchanged.</summary>
public sealed record SubscriptionResponse(
    Guid Id,
    string ServiceName,
    decimal Price,
    DateOnly StartDate,
    DateOnly? TrialEndsAt,
    int Status)
{
    public static SubscriptionResponse From(Subscription s) =>
        new(s.Id, s.ServiceName, s.Price, s.StartDate, s.TrialEndsAt, (int)s.Status);
}
