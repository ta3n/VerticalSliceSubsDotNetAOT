using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.Features.Subscriptions;

public static class SubscriptionProblems
{
    public static ApiProblemException NotFound(Guid id) => new(
        StatusCodes.Status404NotFound, "subscription-not-found", "Subscription not found",
        $"Subscription {id} was not found.",
        new Dictionary<string, object> { ["subscriptionId"] = id.ToString() });

    /// <summary>400 with the original .NET message kept in <c>detail</c> and in the legacy <c>error</c> member.</summary>
    public static ApiProblemException AlreadyCanceled() => new(
        StatusCodes.Status400BadRequest, "subscription-already-canceled", "Subscription already canceled",
        "Subscription already canceled.",
        new Dictionary<string, object> { ["error"] = "Subscription already canceled." });

    public static ApiProblemException InvalidTransition(SubscriptionStatus from, SubscriptionStatus to) => new(
        StatusCodes.Status409Conflict, "invalid-subscription-transition", "Invalid subscription state transition",
        $"Cannot move a {from.ToDb()} subscription to {to.ToDb()}.",
        new Dictionary<string, object> { ["from"] = from.ToDb(), ["to"] = to.ToDb() });

    public static ApiProblemException InvalidCursor() => new(
        StatusCodes.Status400BadRequest, "invalid-cursor", "Invalid pagination cursor",
        "The cursor query parameter is not a valid page token.");

    public static ApiProblemException InvalidStatusFilter(string status) => new(
        StatusCodes.Status400BadRequest, "invalid-subscription-status", "Invalid subscription status filter",
        $"status must be ACTIVE, CANCELED or PAUSED, was: {status}");
}
