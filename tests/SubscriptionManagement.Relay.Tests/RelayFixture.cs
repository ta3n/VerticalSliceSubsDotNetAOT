using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Npgsql;
using SubscriptionManagement.Relay.Features.Outbox;
using SubscriptionManagement.Relay.Infrastructure;
using Testcontainers.PostgreSql;
using Wolverine;

namespace SubscriptionManagement.Relay.Tests;

/// <summary>
/// One real PostgreSQL (Testcontainers) and one Relay host shared by the whole run. The host is composed exactly
/// like Program.cs (<see cref="ServiceSetup"/>), on an in-memory test server and without a broker: messages for
/// RabbitMQ are captured by Wolverine's tracking (<c>App.InvokeMessageAndWaitAsync</c>, <c>App.TrackActivity()</c>).
/// The API is a <see cref="StubApi"/>, and the outbox polling loop is off: a test drains batch by batch itself.
/// </summary>
public sealed class RelayFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    public string PostgresConnectionString => _postgres.GetConnectionString();
    public WebApplication App { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public StubApi Api { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // The table belongs to the API and is created by its migrator: same DDL as migration V6.
        await ExecuteAsync(
            """
            CREATE TABLE outbox_messages (
                id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                type        VARCHAR(200) NOT NULL,
                payload     TEXT         NOT NULL,
                traceparent VARCHAR(55)  NULL,
                occurred_at TIMESTAMP    NOT NULL
            )
            """
        );

        // The content root is the test output directory, where the service's appsettings.json is copied to.
        var builder =
            WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Logging:LogLevel:Default"] = "Warning",
                ["ConnectionStrings:Postgres"] = PostgresConnectionString,
                // Wolverine requires the named connection to exist; with the transports disabled nothing connects to it.
                ["ConnectionStrings:messaging"] = "amqp://guest:guest@127.0.0.1:1",
                ["Api:BaseUrl"] = "https://api.test"
            }
        );

        builder.AddRelay();
        builder.Services.DisableAllExternalWolverineTransports();
        builder.Services.AddHttpClient(nameof(ApiClient)).ConfigurePrimaryHttpMessageHandler(() => Api);
        builder.Services.Remove(builder.Services.Single(d => d.ImplementationType == typeof(OutboxDrain)));

        App = builder.Build();
        App.MapRelay();
        await App.StartAsync();
        Client = App.GetTestClient();

        // Test-only: records every envelope that reaches Wolverine's durable outbox table, in the transaction that
        // inserts it. The table itself cannot be asserted on, because the sending agent deletes a row once it is sent.
        await ExecuteAsync(
            """
            CREATE TABLE stored_envelopes (id UUID NOT NULL);
            CREATE FUNCTION record_stored_envelope() RETURNS trigger LANGUAGE plpgsql AS
                $$ BEGIN INSERT INTO stored_envelopes VALUES (NEW.id); RETURN NEW; END $$;
            CREATE TRIGGER record_stored_envelope AFTER INSERT ON relay.wolverine_outgoing_envelopes
                FOR EACH ROW EXECUTE FUNCTION record_stored_envelope();
            """
        );
    }

    /// <summary>The service's drain, not started: a test calls <c>DrainBatchAsync</c> (or starts the loop) itself.</summary>
    public OutboxDrain CreateDrain(
        int? batchSize = null,
        int? pollIntervalMs = null,
        ILogger<OutboxDrain>? logger = null,
        TimeProvider? time = null
    )
    {
        var configured = App.Services.GetRequiredService<RelayOptions>();
        var options = new RelayOptions
        {
            BatchSize = batchSize ?? configured.BatchSize,
            PollIntervalMs = pollIntervalMs ?? configured.PollIntervalMs
        };
        return new OutboxDrain(
            App.Services.GetRequiredService<IServiceScopeFactory>(),
            options,
            logger ?? NullLogger<OutboxDrain>.Instance,
            time ?? TimeProvider.System
        );
    }

    public async Task<NpgsqlConnection> OpenDbAsync()
    {
        var connection = new NpgsqlConnection(PostgresConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    public async Task ExecuteAsync(
        string sql
    )
    {
        await using var connection = await OpenDbAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<T> ScalarAsync<T>(
        string sql
    )
    {
        await using var connection = await OpenDbAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (App is not null)
        {
            await App.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }
}

/// <summary>All Relay tests share one database and one host, and run sequentially.</summary>
[CollectionDefinition(Name)]
public sealed class RelayCollection : ICollectionFixture<RelayFixture>
{
    public const string Name = "relay";
}