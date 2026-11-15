// Local orchestration with Aspire: PostgreSQL + Redis + RabbitMQ + migrator + API + billing/payments/relay.
// The .NET projects run from source (JIT, debuggable) and all telemetry lands in the Aspire dashboard — and,
// unless Observability:Enabled=false, also in Prometheus/Tempo/Loki behind Grafana (see Observability.cs).
//
//   dotnet run --project src/AppHost      # or: aspire run
//   dotnet run --project src/AppHost -- --Observability:Enabled=false   # lean: Aspire dashboard only
//   dotnet run --project src/AppHost -- --LoadTest:AutoStart=smoke      # run a k6 scenario right after startup
//
// Operational settings (connection limits, ports, load-test sizes) live in appsettings.json; see Settings.cs.

using AppHost;
using Projects;

var builder = DistributedApplication.CreateBuilder(args);
var (maxConnections, apiPoolSize, servicePoolSize) = builder.Configuration.PostgresConnectionLimits();

// Image tags match compose.yaml / the integration tests; the data volume keeps data across AppHost restarts.
// Postgres:MaxConnections replaces the image default (100) so the API pool below fits; keep in step with compose.yaml.
var postgres = builder.AddPostgres("postgres")
    .WithImageTag("17")
    .WithArgs("-c", $"max_connections={maxConnections}")
    .WithReplication()
    .WithDataVolume();
var database = postgres.AddDatabase("subscriptions");

// Two streaming replicas (primary + 2 = 3 PostgreSQL instances) and PgBouncer in front of the primary for the API.
var standbys = builder.AddStandbys(postgres, maxConnections);
var pgbouncer = builder.Configuration.Required<bool>("PgBouncer:Enabled") ? builder.AddPgBouncer(postgres) : null;

var redis = builder.AddRedis("redis")
    .WithImageTag("7-alpine");

// The services read ConnectionStrings:Postgres / ConnectionStrings:Redis, hence the explicit connection names.
var migrator = builder.AddProject<SubscriptionManagement_Migrator>("migrator")
    .WithReference(database, "Postgres")
    .WaitFor(database);

// The API connects through PgBouncer (transaction pooling) unless PgBouncer:Enabled=false: its Npgsql pool
// (Api:PostgresMaxPoolSize) is then only the client side, PgBouncer:PoolSize server connections do the work. The
// migrator and the s2s services always stay on the primary.
var apiPostgres = pgbouncer is null
    ? ReferenceExpression.Create(
        $"{database.Resource.ConnectionStringExpression};Maximum Pool Size={apiPoolSize.ToString()}"
    )
    : PostgresCluster.ApiConnectionString(pgbouncer, postgres, database, apiPoolSize);

var api = builder.AddProject<SubscriptionManagement_Api>("subscription-management")
    .WithEnvironment("ConnectionStrings__Postgres", apiPostgres)
    .WithReferenceRelationship(database)
    .WaitFor(database)
    .WithReference(redis, "Redis")
    .WaitFor(redis)
    .WaitForCompletion(migrator)
    .WithHttpHealthCheck("/health/ready")
    // Two instances (Replicas) behind Aspire's round-robin endpoint proxy; each owns its Npgsql pool.
    .WithReplicas(builder.Configuration.Required<int>("Replicas"))
    // No appsettings hot-reload under the AppHost: it needs an inotify instance per file, and a desktop with a few
    // IDEs open is often at the per-user limit (128) — the API then crashes at startup with an IOException.
    .WithEnvironment("DOTNET_hostBuilder__reloadConfigOnChange", "false")
    .WithUrlForEndpoint("http", _ => new() { Url = "/scalar", DisplayText = "Scalar API reference" });

var services = builder.AddS2SServices(postgres, database, migrator, api, servicePoolSize);
if (pgbouncer is not null) api.WaitFor(pgbouncer);
api.WithEnvironment("Billing__GrpcAddress", services.Billing.GetEndpoint("grpc"))
    .WithEnvironment("Outbox__Enabled", builder.Configuration.Required<bool>("Api:OutboxEnabled").ToString())
    .WaitFor(services.Billing);

// Prometheus/Tempo/Loki/Grafana + exporters; without it only the Aspire dashboard receives telemetry.
ObservabilityStack? stack = null;
if (builder.Configuration.Required<bool>("Observability:Enabled"))
{
    stack = builder.AddObservabilityStack(postgres, redis, standbys, pgbouncer);
    api.WaitFor(stack.Collector);
    services.Billing.WaitFor(stack.Collector);
    services.Payments.WaitFor(stack.Collector);
    services.Relay.WaitFor(stack.Collector);
}

// k6 scenarios from scripts/*-test.js as on-demand resources (▶ in the dashboard); see LoadTests.cs.
builder.AddK6Suite(api, stack, services);

await builder.Build().RunAsync();