namespace SubscriptionManagement.Features.Subscriptions;

/// <summary>Numeric values are the wire contract (original API: Active = 1, Canceled = 2).</summary>
public enum SubscriptionStatus
{
    Active = 1,
    Canceled = 2,
    Paused = 3
}

public static class SubscriptionStatusNames
{
    // The database stores the enum name in upper case, matching the Java reference schema (V1).
    public static string ToDb(this SubscriptionStatus status) => status switch
    {
        SubscriptionStatus.Active => "ACTIVE",
        SubscriptionStatus.Canceled => "CANCELED",
        SubscriptionStatus.Paused => "PAUSED",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    public static SubscriptionStatus FromDb(string value) => TryParse(value, out var status)
        ? status
        : throw new InvalidOperationException($"Unknown subscription status '{value}' in database.");

    /// <summary>Exact, case-sensitive match on ACTIVE / CANCELED / PAUSED (Java parity).</summary>
    public static bool TryParse(string? value, out SubscriptionStatus status)
    {
        switch (value)
        {
            case "ACTIVE": status = SubscriptionStatus.Active; return true;
            case "CANCELED": status = SubscriptionStatus.Canceled; return true;
            case "PAUSED": status = SubscriptionStatus.Paused; return true;
            default: status = default; return false;
        }
    }
}
