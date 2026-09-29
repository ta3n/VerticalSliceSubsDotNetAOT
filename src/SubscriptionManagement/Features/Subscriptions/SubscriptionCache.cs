using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.Features.Subscriptions;

/// <summary>
/// Cache-aside view of <see cref="SubscriptionResponse"/> keyed by subscription id: filled on read and on
/// create/pause/resume/renew, dropped on cancel. Backend failures are logged and treated as a miss so a Redis
/// outage degrades to database reads (entries expire after the TTL either way).
/// </summary>
public sealed class SubscriptionCache(
    IDistributedCache cache,
    IOptions<CacheOptions> options,
    ILogger<SubscriptionCache> logger)
{
    private static string Key(Guid id) => $"subscriptions:{id}";

    public async Task<SubscriptionResponse?> GetAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var bytes = await cache.GetAsync(Key(id), ct);
            return bytes is null
                ? null
                : JsonSerializer.Deserialize(bytes, AppJsonSerializerContext.Default.SubscriptionResponse);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cache read failed for subscription {SubscriptionId}", id);
            return null;
        }
    }

    public async Task PutAsync(SubscriptionResponse response, CancellationToken ct)
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(response, AppJsonSerializerContext.Default.SubscriptionResponse);
            await cache.SetAsync(Key(response.Id), bytes,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = options.Value.SubscriptionTtl }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cache write failed for subscription {SubscriptionId}", response.Id);
        }
    }

    public async Task EvictAsync(Guid id, CancellationToken ct)
    {
        try
        {
            await cache.RemoveAsync(Key(id), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cache eviction failed for subscription {SubscriptionId}", id);
        }
    }
}
