using Microsoft.Extensions.Configuration;

namespace AppHost;

/// <summary>
/// Operational settings of the AppHost. The values live in <c>appsettings.json</c>; any of them can be overridden the
/// usual way: <c>--Postgres:MaxConnections=500</c>, <c>Postgres__MaxConnections=500</c> or user secrets.
/// </summary>
internal static class Settings
{
    // superuser_reserved_connections (PostgreSQL default): never available to the API's pool.
    private const int ReservedConnections = 3;

    /// <summary>The value at <paramref name="key"/>; a missing or empty one is a configuration error, not a default.</summary>
    public static T Required<T>(this IConfiguration configuration, string key) =>
        string.IsNullOrWhiteSpace(configuration[key])
            ? throw new InvalidOperationException($"Missing setting '{key}' (see src/AppHost/appsettings.json).")
            : configuration.GetValue<T>(key)!;

    /// <summary>
    /// PostgreSQL's <c>max_connections</c> and the API's Npgsql pool size. A pool that does not fit fails under load
    /// with 53300 "too many clients already", so it is rejected at startup (the Helm chart has the same guard).
    /// </summary>
    public static (int MaxConnections, int ApiPoolSize) PostgresConnectionLimits(this IConfiguration configuration)
    {
        var maxConnections = configuration.Required<int>("Postgres:MaxConnections");
        var poolSize = configuration.Required<int>("Api:PostgresMaxPoolSize");
        var available = maxConnections - ReservedConnections;
        if (poolSize < 1 || poolSize >= available)
        {
            throw new InvalidOperationException(
                $"Api:PostgresMaxPoolSize ({poolSize}) must be between 1 and {available - 1}: Postgres:MaxConnections " +
                $"({maxConnections}) minus {ReservedConnections} superuser-reserved slots, leaving room for the migrator, " +
                "postgres-exporter and psql sessions.");
        }

        return (maxConnections, poolSize);
    }
}
