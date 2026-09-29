using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace SubscriptionManagement.Infrastructure;

public sealed class DatabaseHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("database unreachable", ex);
        }
    }
}

public sealed class CacheHealthCheck(IDistributedCache cache) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await cache.GetAsync("health-probe", ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("cache unreachable", ex);
        }
    }
}

public static class HealthEndpoints
{
    /// <summary>
    /// <c>/health/live</c>: process is up. <c>/health/ready</c>: database only (Redis is optional and must
    /// not take the instance out of rotation, as in the Java reference). <c>/health</c>: all components.
    /// </summary>
    public static void MapHealth(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health/live", new() { Predicate = _ => false, ResponseWriter = Write });
        app.MapHealthChecks("/health/ready", new() { Predicate = r => r.Tags.Contains("ready"), ResponseWriter = Write });
        app.MapHealthChecks("/health", new() { ResponseWriter = Write });
    }

    private static async Task Write(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        await using (var writer = new Utf8JsonWriter(context.Response.BodyWriter))
        {
            writer.WriteStartObject();
            writer.WriteString("status", report.Status == HealthStatus.Healthy ? "UP" : "DOWN");
            writer.WriteEndObject();
        }

        await context.Response.BodyWriter.FlushAsync();
    }
}
