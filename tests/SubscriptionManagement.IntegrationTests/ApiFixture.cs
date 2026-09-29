using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using StackExchange.Redis;
using SubscriptionManagement.Migrator;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace SubscriptionManagement.IntegrationTests;

/// <summary>
/// One real PostgreSQL + Redis (Testcontainers) shared by the whole run. The schema is created by the real
/// migrator, and the API is hosted in-process through <see cref="WebApplicationFactory{TEntryPoint}"/>.
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

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        using var loggerFactory = LoggerFactory.Create(_ => { });
        await MigrationRunner.RunAsync(PostgresConnectionString, loggerFactory.CreateLogger("migrator"));

        Factory = CreateFactory(redis: true);
        Client = Factory.CreateClient();
        Redis = await ConnectionMultiplexer.ConnectAsync(RedisConnectionString);
    }

    public WebApplicationFactory<Program> CreateFactory(bool redis) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var settings = new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = PostgresConnectionString };
                if (redis)
                {
                    settings["ConnectionStrings:Redis"] = RedisConnectionString;
                }

                config.AddInMemoryCollection(settings);
            }));

    public async Task<NpgsqlConnection> OpenDbAsync()
    {
        var connection = new NpgsqlConnection(PostgresConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenDbAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        Redis?.Dispose();
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }
}

/// <summary>All API tests share one database and run sequentially, so tests that need an empty table can truncate it.</summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
