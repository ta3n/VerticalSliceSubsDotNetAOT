using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SubscriptionManagement.Billing.Infrastructure;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Tracking;

namespace SubscriptionManagement.Billing.Tests;

/// <summary>
/// One real PostgreSQL (Testcontainers) and one Billing host shared by the whole run. The host is composed exactly
/// like Program.cs (<see cref="ServiceSetup"/>), on an in-memory test server and without a broker: messages for
/// RabbitMQ are captured by Wolverine's tracking (<c>App.InvokeMessageAndWaitAsync</c>, <c>App.TrackActivity()</c>).
/// </summary>
public sealed class BillingFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    public string PostgresConnectionString => _postgres.GetConnectionString();
    public WebApplication App { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;

    public const int RenewalTimeoutSeconds = 3600;

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
                // Far beyond the length of a test run: a renewal timeout only fires when a test plays it.
                ["Billing:RenewalTimeoutSeconds"] = RenewalTimeoutSeconds.ToString()
            }
        );

        builder.AddBilling();
        builder.Services.DisableAllExternalWolverineTransports();

        App = builder.Build();
        App.MapBilling();
        await App.StartAsync();
        Client = App.GetTestClient();
    }

    /// <summary>
    /// A tracked session: it completes when the message and everything it cascades into have been handled, and
    /// records what was sent. The timeout is generous because the first use of a handler compiles it.
    /// </summary>
    public TrackedSessionConfiguration Track()
    {
        return App.TrackActivity(TimeSpan.FromSeconds(30));
    }

    /// <summary>Runs a query on a fresh <see cref="BillingDbContext"/>, i.e. against what is committed.</summary>
    public async Task<T> QueryAsync<T>(
        Func<BillingDbContext, Task<T>> query
    )
    {
        await using var scope = App.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<BillingDbContext>());
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
        if (App is not null) await App.DisposeAsync();

        await _postgres.DisposeAsync();
    }
}

/// <summary>All Billing tests share one database and one host, and run sequentially.</summary>
[CollectionDefinition(Name)]
public sealed class BillingCollection : ICollectionFixture<BillingFixture>
{
    public const string Name = "billing";
}