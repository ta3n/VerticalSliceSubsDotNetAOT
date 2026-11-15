using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using StackExchange.Redis;
using SubscriptionManagement.Infrastructure.Billing;
using SubscriptionManagement.Migrator;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace SubscriptionManagement.IntegrationTests;

/// <summary>
/// One real PostgreSQL + Redis (Testcontainers) shared by the whole run. The schema is created by the real
/// migrator, and the API is hosted in-process through <see cref="WebApplicationFactory{TEntryPoint}"/>. The shared
/// host runs with the service-to-service features on: the outbox enabled and Billing replaced by <see cref="Billing"/>.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();

    public string PostgresConnectionString => _postgres.GetConnectionString();
    public string RedisConnectionString => _redis.GetConnectionString();
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public IConnectionMultiplexer Redis { get; private set; } = null!;
    public FakeBillingGateway Billing { get; } = new();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        using var loggerFactory = LoggerFactory.Create(_ => { });
        await MigrationRunner.RunAsync(PostgresConnectionString, loggerFactory.CreateLogger("migrator"));

        Factory = CreateFactory(true, billing: Billing);
        Client = Factory.CreateClient();
        Redis = await ConnectionMultiplexer.ConnectAsync(RedisConnectionString);
    }

    /// <param name="redis">Use the Redis container (otherwise the in-process cache).</param>
    /// <param name="postgres">Overrides the PostgreSQL connection string (e.g. an unreachable server or "").</param>
    /// <param name="settings">Host settings that must be visible while Program.cs builds the app (e.g. OTEL_*).</param>
    /// <param name="billing">Stands in for the Billing service and turns the outbox on. Without it both stay as
    /// shipped: no Billing address and the outbox off.</param>
    public WebApplicationFactory<Program> CreateFactory(
        bool redis,
        string? postgres = null,
        IReadOnlyDictionary<string, string>? settings = null,
        IBillingGateway? billing = null
    )
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(
            builder =>
            {
                foreach (var (key, value) in settings ?? new Dictionary<string, string>())
                    builder.UseSetting(key, value);

                builder.ConfigureAppConfiguration(
                    (_, config) =>
                    {
                        var values = new Dictionary<string, string?>
                            { ["ConnectionStrings:Postgres"] = postgres ?? PostgresConnectionString };
                        // Always set explicitly: appsettings.Development.json points at localhost:6379 for local runs.
                        values["ConnectionStrings:Redis"] = redis ? RedisConnectionString : "";
                        if (billing is not null) values["Outbox:Enabled"] = "true";

                        config.AddInMemoryCollection(values);
                    }
                );

                if (billing is { } gateway) builder.ConfigureTestServices(services => services.AddSingleton(gateway));
            }
        );
    }

    public async Task<NpgsqlConnection> OpenDbAsync()
    {
        var connection = new NpgsqlConnection(PostgresConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    public async Task ExecuteAsync(
        string sql,
        params (string Name, object Value)[] parameters
    )
    {
        await using var connection = await OpenDbAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// The outbox messages about one subscription, oldest first. Filtered by id because the table is shared by
    /// every test and nothing drains it here.
    /// </summary>
    public async Task<List<OutboxRow>> OutboxForAsync(
        string subscriptionId
    )
    {
        await using var connection = await OpenDbAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT type, payload, traceparent, occurred_at FROM outbox_messages
            WHERE payload::jsonb ->> 'subscriptionId' = $1 ORDER BY id
            """,
            connection
        );
        command.Parameters.AddWithValue(subscriptionId);
        var rows = new List<OutboxRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            using var payload = JsonDocument.Parse(reader.GetString(1));
            rows.Add(
                new OutboxRow(
                    reader.GetString(0),
                    payload.RootElement.Clone(),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc)
                )
            );
        }

        return rows;
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (Factory is not null) await Factory.DisposeAsync();

        Redis?.Dispose();
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }
}

public sealed record OutboxRow(
    string Type,
    JsonElement Payload,
    string? Traceparent,
    DateTime OccurredAt
);

/// <summary>All API tests share one database and run sequentially, so tests that need an empty table can truncate it.</summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}