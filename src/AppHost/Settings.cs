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
        /// PostgreSQL's <c>max_connections</c> and the Npgsql pool sizes. The primary's server connections are the
        /// PgBouncer pool (the API) plus <c>Replicas</c> × 3 service pools (Billing, Payments, Relay, direct); the API's
        /// own pool is only the client side of PgBouncer and must fit its <c>max_client_conn</c>. A budget that does not
        /// fit fails under load with 53300 "too many clients already" (PostgreSQL) or "no more connections allowed"
        /// (PgBouncer), so it is rejected at startup (the Helm chart has the same guard for PostgreSQL).
        /// </summary>
        public (int MaxConnections, int ApiPoolSize, int ServicePoolSize) PostgresConnectionLimits()
        {
            var replicas = configuration.Required<int>("Replicas");
            var maxConnections = configuration.Required<int>("Postgres:MaxConnections");
            var poolSize = configuration.Required<int>("Api:PostgresMaxPoolSize");
            var servicePoolSize = configuration.Required<int>("Services:PostgresMaxPoolSize");
            var bouncerPoolSize = configuration.Required<int>("PgBouncer:PoolSize");
            var maxClients = configuration.Required<int>("PgBouncer:MaxClientConnections");
            var available = maxConnections - ReservedConnections;
            // Behind PgBouncer the API takes the pooler's server connections; without it every API instance's own pool.
            var apiServerConnections = configuration.Required<bool>("PgBouncer:Enabled")
                ? bouncerPoolSize
                : replicas * (long)poolSize;
            var serverConnections = apiServerConnections + replicas * 3L * servicePoolSize;
            if (replicas < 1
                || poolSize < 1
                || servicePoolSize < 1
                || bouncerPoolSize < 1
                || serverConnections >= available
                || (configuration.Required<bool>("PgBouncer:Enabled") && replicas * (long)poolSize > maxClients))
            {
                throw new InvalidOperationException(
                    $"Positive replicas and pool sizes are required, PgBouncer:PoolSize ({bouncerPoolSize}) + Replicas "
                    + $"({replicas}) × 3 × Services:PostgresMaxPoolSize ({servicePoolSize}) must be below {available} "
                    + $"(Postgres:MaxConnections ({maxConnections}) minus {ReservedConnections} superuser-reserved "
                    + "slots, leaving room for the migrator, postgres-exporter and psql sessions), and Replicas "
                    + $"({replicas}) × Api:PostgresMaxPoolSize ({poolSize}) must not exceed PgBouncer:MaxClientConnections "
                    + $"({maxClients})."
                );
            }

            return (maxConnections, poolSize, servicePoolSize);
        }
    }
}