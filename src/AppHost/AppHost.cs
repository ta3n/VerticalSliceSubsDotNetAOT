// Local orchestration with Aspire: the same topology as compose.yaml (PostgreSQL + Redis + one-shot migrator + API),
// but the two .NET projects run from source (JIT, debuggable) and all telemetry lands in the Aspire dashboard — and,
// unless Observability:Enabled=false, also in Prometheus/Tempo/Loki behind Grafana (see Observability.cs).
//
//   dotnet run --project src/AppHost      # or: aspire run
//   dotnet run --project src/AppHost -- --Observability:Enabled=false   # lean: Aspire dashboard only
//   dotnet run --project src/AppHost -- --LoadTest:AutoStart=smoke      # run a k6 scenario right after startup
//
// Operational settings (connection limits, ports, load-test sizes) live in appsettings.json; see Settings.cs.

using AppHost;

var builder = DistributedApplication.CreateBuilder(args);
var (maxConnections, apiPoolSize) = builder.Configuration.PostgresConnectionLimits();

// Image tags match compose.yaml / the integration tests; the data volume keeps data across AppHost restarts.
// Postgres:MaxConnections replaces the image default (100) so the API pool below fits; keep in step with compose.yaml.
var postgres = builder.AddPostgres("postgres")
    .WithImageTag("17")
    .WithArgs("-c", $"max_connections={maxConnections}")
    .WithDataVolume();
var database = postgres.AddDatabase("subscriptions");

var redis = builder.AddRedis("redis")
    .WithImageTag("7-alpine");

// The services read ConnectionStrings:Postgres / ConnectionStrings:Redis, hence the explicit connection names.
var migrator = builder.AddProject<Projects.SubscriptionManagement_Migrator>("migrator")
    .WithReference(database, "Postgres")
    .WaitFor(database);

// Pool capped (Api:PostgresMaxPoolSize) like compose.yaml, below Postgres' max_connections: the remaining slots cover
// the superuser-reserved ones, the migrator, postgres-exporter and ad-hoc psql sessions.
var apiPostgres = ReferenceExpression.Create(
    $"{database.Resource.ConnectionStringExpression};Maximum Pool Size={apiPoolSize.ToString()}");

var api = builder.AddProject<Projects.SubscriptionManagement>("subscription-management")
    .WithEnvironment("ConnectionStrings__Postgres", apiPostgres)
    .WithReferenceRelationship(database)
    .WaitFor(database)
    .WithReference(redis, "Redis")
    .WaitFor(redis)
    .WaitForCompletion(migrator)
    .WithHttpHealthCheck("/health/ready")
    // No appsettings hot-reload under the AppHost: it needs an inotify instance per file, and a desktop with a few
    // IDEs open is often at the per-user limit (128) — the API then crashes at startup with an IOException.
    .WithEnvironment("DOTNET_hostBuilder__reloadConfigOnChange", "false")
    .WithUrlForEndpoint("http", _ => new() { Url = "/scalar", DisplayText = "Scalar API reference" });

// Prometheus/Tempo/Loki/Grafana + exporters; without it only the Aspire dashboard receives telemetry.
ObservabilityStack? stack = null;
if (builder.Configuration.Required<bool>("Observability:Enabled"))
{
    stack = builder.AddObservabilityStack(postgres, redis);
    api.WaitFor(stack.Collector);
}

// k6 scenarios from scripts/*-test.js as on-demand resources (▶ in the dashboard); see LoadTests.cs.
builder.AddK6Suite(api, stack);

await builder.Build().RunAsync();
