namespace SubscriptionManagement.Infrastructure;

public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    /// <summary>Lifetime of a cached subscription view (Java reference: 5 minutes).</summary>
    public TimeSpan SubscriptionTtl { get; set; } = TimeSpan.FromMinutes(5);
}
