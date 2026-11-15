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

    extension(
        IConfiguration configuration
    )
    {
        /// <summary>The value at <paramref name="key"/>; a missing or empty one is a configuration error, not a default.</summary>
        public T Required<T>(
            string key
        )
        {
            return string.IsNullOrWhiteSpace(configuration[key])
                ? throw new InvalidOperationException($"Missing setting '{key}' (see src/AppHost/appsettings.json).")
                : configuration.GetValue<T>(key)!;
        }

        /// <summary>
        /// PostgreSQL's <c>max_connections</c> and the API's Npgsql pool size. A pool that does not fit fails under load
        /// with 53300 "too many clients already", so it is rejected at startup (the Helm chart has the same guard).
        /// </summary>
        public (int MaxConnections, int ApiPoolSize, int ServicePoolSize) PostgresConnectionLimits()
        {
            var maxConnections = configuration.Required<int>("Postgres:MaxConnections");
            var poolSize = configuration.Required<int>("Api:PostgresMaxPoolSize");
            var servicePoolSize = configuration.Required<int>("Services:PostgresMaxPoolSize");
            var available = maxConnections - ReservedConnections;
            if (poolSize < 1 || servicePoolSize < 1 || (long)poolSize + 3L * servicePoolSize >= available)
                throw new InvalidOperationException(
                    $"Positive pool sizes are required and Api:PostgresMaxPoolSize ({poolSize}) + 3 × Services:PostgresMaxPoolSize "
                    + $"({servicePoolSize}) must be below {available}: Postgres:MaxConnections ({maxConnections}) minus "
                    + $"{ReservedConnections} superuser-reserved slots, leaving room for the migrator, "
                    + "postgres-exporter and psql sessions."
                );

            return (maxConnections, poolSize, servicePoolSize);
        }
    }
}