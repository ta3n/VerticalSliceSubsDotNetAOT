using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SubscriptionManagement.Payments.Features.Payments;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Tracking;

namespace SubscriptionManagement.Payments.Tests;

/// <summary>
/// One real PostgreSQL (Testcontainers) and one Payments host shared by the whole run. The host is composed exactly
/// like Program.cs (<see cref="ServiceSetup"/>), on an in-memory test server and without a broker: messages for
/// RabbitMQ are captured by Wolverine's tracking (<c>App.InvokeMessageAndWaitAsync</c>, <c>App.TrackActivity()</c>).
/// </summary>
public sealed class PaymentsFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    public string PostgresConnectionString => _postgres.GetConnectionString();
    public WebApplication App { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

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
                // Not the appsettings.json values, so the tests show the thresholds are read from configuration.
                ["Gateway:DeclineAtOrAbove"] = "100",
                ["Gateway:NoReplyAtOrAbove"] = "200.50"
            }
        );

        builder.AddPayments();
        builder.Services.DisableAllExternalWolverineTransports();

        App = builder.Build();
        App.MapPayments();
        await App.StartAsync();
        Client = App.GetTestClient();
    }

    /// <summary>
    /// Runs a message through its handler like a delivery from the broker and waits for the work it causes. The
    /// timeout covers the first call of each handler, which compiles its generated code.
    /// </summary>
    public Task<ITrackedSession> DeliverAsync(
        object message
    )
    {
        return App.InvokeMessageAndWaitAsync(message, 60_000);
    }

    /// <summary>
    /// Delivers the messages in parallel, each on its own message bus, inside one tracked session (tracked sessions
    /// are host-wide and cannot overlap).
    /// </summary>
    public Task<ITrackedSession> DeliverConcurrentlyAsync(
        params object[] messages
    )
    {
        return App.ExecuteAndWaitAsync(
            () => Task.WhenAll(
                messages.Select(
                    async message =>
                    {
                        await using var scope = App.Services.CreateAsyncScope();
                        await scope.ServiceProvider.GetRequiredService<IMessageBus>().InvokeAsync(message);
                    }
                )
            ),
            60_000
        );
    }

    /// <summary>The events of a payment stream, oldest first.</summary>
    public async Task<IReadOnlyList<object>> StreamAsync(
        Guid paymentId
    )
    {
        await using var session = App.Services.GetRequiredService<IDocumentStore>().QuerySession();
        return [.. (await session.Events.FetchStreamAsync(paymentId)).Select(e => e.Data)];
    }

    /// <summary>The stored inline snapshot, null when the payment has no stream.</summary>
    public async Task<Payment?> SnapshotAsync(
        Guid paymentId
    )
    {
        await using var session = App.Services.GetRequiredService<IDocumentStore>().QuerySession();
        return await session.LoadAsync<Payment>(paymentId);
    }

    public async Task<NpgsqlConnection> OpenDbAsync()
    {
        var connection = new NpgsqlConnection(PostgresConnectionString);
        await connection.OpenAsync();
        return connection;
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

/// <summary>All Payments tests share one database and one host, and run sequentially.</summary>
[CollectionDefinition(Name)]
public sealed class PaymentsCollection : ICollectionFixture<PaymentsFixture>
{
    public const string Name = "payments";
}