extern alias ApiHost;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Npgsql;
using SubscriptionManagement.Billing;
using SubscriptionManagement.Migrator;
using SubscriptionManagement.Payments;
using SubscriptionManagement.Relay;
using SubscriptionManagement.Relay.Infrastructure;
using Testcontainers.PostgreSql;

namespace SubscriptionManagement.SystemTests;

/// <summary>
/// The whole system in one process on real infrastructure: PostgreSQL and RabbitMQ from Testcontainers, the real
/// migrator, and the four services composed exactly like their Program.cs files (<c>AddBilling</c>, <c>AddPayments</c>,
/// <c>AddRelay</c>, and the API through <see cref="WebApplicationFactory{TEntryPoint}"/>). They talk to each other the
/// way they do under the AppHost: through the broker, the API's outbox table and gRPC. Only the HTTP/2 hop between
/// the API and Billing runs over the in-memory test server instead of a socket.
/// <para>
/// Databases mirror the AppHost: <c>subs</c> (API + Relay), <c>billing</c>, <c>payments</c>. The renewal timeout is
/// short (<see cref="RenewalTimeoutSeconds"/>) so the timeout path can be played; the other paths finish in a
/// fraction of it once the handlers have been compiled, which the warm-up in <see cref="InitializeAsync"/> does.
/// </para>
/// </summary>
public sealed class SystemFixture : IAsyncLifetime
{
  public const int RenewalTimeoutSeconds = 10;

  // The simulated gateway decides by amount (tax 10 % included): 10 -> captured, 6000 -> declined, 9500 -> no reply.
  public const decimal CapturedPrice = 10m;
  public const decimal DeclinedPrice = 6000m;
  public const decimal SilentPrice = 9500m;

  private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(3);

  private readonly IContainer _broker = new ContainerBuilder("rabbitmq:4.3-management")
    .WithEnvironment("RABBITMQ_DEFAULT_USER", "test")
    .WithEnvironment("RABBITMQ_DEFAULT_PASS", "test")
    .WithPortBinding(5672, true)
    .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5672))
    .Build();

  private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

  private WebApplication _billing = null!;
  private Uri _brokerAddress = null!;
  private WebApplication _payments = null!;
  private WebApplication? _relay;

  public WebApplicationFactory<ApiHost::Program> ApiFactory { get; private set; } = null!;
  public HttpClient Api { get; private set; } = null!;
  public HttpClient Billing { get; private set; } = null!;
  public HttpClient Payments { get; private set; } = null!;
  public string ApiConnectionString { get; private set; } = null!;

  public async Task InitializeAsync()
  {
    await Task.WhenAll(_postgres.StartAsync(), _broker.StartAsync());
    _brokerAddress = new Uri($"amqp://test:test@{_broker.Hostname}:{_broker.GetMappedPublicPort(5672)}");

    ApiConnectionString = await CreateDatabaseAsync("subs");
    var billingConnectionString = await CreateDatabaseAsync("billing");
    var paymentsConnectionString = await CreateDatabaseAsync("payments");
    using (var loggerFactory = LoggerFactory.Create(_ => { }))
    {
      await MigrationRunner.RunAsync(ApiConnectionString, loggerFactory.CreateLogger("migrator"));
    }

    _billing = await StartServiceAsync(
      billingConnectionString,
      new Dictionary<string, string?>
      {
        ["Billing:RenewalTimeoutSeconds"] = RenewalTimeoutSeconds.ToString(),
        ["Billing:TaxRate"] = "0.10"
      },
      builder => builder.AddBilling(),
      app => app.MapBilling()
    );
    _payments = await StartServiceAsync(
      paymentsConnectionString,
      new Dictionary<string, string?>(),
      builder => builder.AddPayments(),
      app => app.MapPayments()
    );
    Billing = _billing.GetTestClient();
    Payments = _payments.GetTestClient();

    var billingServer = _billing.GetTestServer();
    ApiFactory = new WebApplicationFactory<ApiHost::Program>().WithWebHostBuilder(
      builder =>
      {
        // Read while Program.cs builds the app, so these go in as host settings.
        builder.UseSetting("Billing:GrpcAddress", "https://billing.test");
        builder.UseSetting("Outbox:Enabled", "true");
        builder.ConfigureAppConfiguration(
          (_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
              ["Logging:LogLevel:Default"] = "Warning",
              ["ConnectionStrings:Postgres"] = ApiConnectionString,
              // No Redis: the API falls back to its in-process cache, which is not what these tests probe.
              ["ConnectionStrings:Redis"] = ""
            }
          )
        );
        // The only HttpClient of the API is the generated gRPC client: point it at the Billing test server.
        builder.ConfigureTestServices(
          services => services.ConfigureAll<HttpClientFactoryOptions>(
            o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = billingServer.CreateHandler())
          )
        );
      }
    );
    Api = ApiFactory.CreateClient();

    await StartRelayAsync();
    await WarmUpAsync();
  }

  public async Task DisposeAsync()
  {
    Api?.Dispose();
    await StopRelayAsync();
    if (ApiFactory is not null) await ApiFactory.DisposeAsync();

    Billing?.Dispose();
    Payments?.Dispose();
    if (_billing is not null) await _billing.DisposeAsync();

    if (_payments is not null) await _payments.DisposeAsync();

    await _broker.DisposeAsync();
    await _postgres.DisposeAsync();
  }

  /// <summary>The Relay in this process. Stopped and started again by the outbox durability test.</summary>
  public async Task StartRelayAsync()
  {
    if (_relay is not null) return;

    var apiServer = ApiFactory.Server;
    _relay = await StartServiceAsync(
      ApiConnectionString,
      new Dictionary<string, string?> { ["Api:BaseUrl"] = "https://api.test", ["Relay:PollIntervalMs"] = "100" },
      builder =>
      {
        builder.AddRelay();
        builder.Services.AddHttpClient(nameof(ApiClient))
          .ConfigurePrimaryHttpMessageHandler(() => new TraceparentHandler(apiServer.CreateHandler()));
      },
      app => app.MapRelay()
    );
  }

  public async Task StopRelayAsync()
  {
    if (_relay is null) return;

    var relay = _relay;
    _relay = null;
    await relay.StopAsync();
    await relay.DisposeAsync();
  }

  private async Task<WebApplication> StartServiceAsync(
    string postgres,
    Dictionary<string, string?> settings,
    Action<WebApplicationBuilder> add,
    Action<WebApplication> map
  )
  {
    // The content root is the test output directory; every setting a service reads is given explicitly below.
    var builder =
      WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
    builder.WebHost.UseTestServer();
    settings["Logging:LogLevel:Default"] = "Warning";
    settings["ConnectionStrings:Postgres"] = postgres;
    settings["ConnectionStrings:messaging"] = _brokerAddress.ToString();
    builder.Configuration.AddInMemoryCollection(settings);

    add(builder);
    var app = builder.Build();
    map(app);
    using var startup = new CancellationTokenSource(StartupTimeout);
    await app.StartAsync(startup.Token);
    return app;
  }

  /// <summary>One renewal of each kind, so the first test does not pay for compiling the message handlers.</summary>
  private async Task WarmUpAsync()
  {
    foreach (var price in new[] { CapturedPrice, DeclinedPrice })
    {
      var created = await Api.PostAsJsonAsync("/subscriptions", NewSubscription("Warm-up", price));
      var subscription = await created.Content.ReadFromJsonAsync<JsonElement>();
      var started = await Api.PostAsync($"/subscriptions/{subscription.GetProperty("id").GetString()}/renewals", null);
      var invoiceId = (await started.Content.ReadFromJsonAsync<JsonElement>())
        .GetProperty("invoice")
        .GetProperty("id")
        .GetString();
      await Eventually.UntilAsync(
        async () =>
        {
          var response = await Api.GetAsync($"/invoices/{invoiceId}/renewal");
          if (!response.IsSuccessStatusCode) return (false, default);

          var body = await response.Content.ReadFromJsonAsync<JsonElement>();
          return (SystemTestBase.Terminal.Contains(body.GetProperty("state").GetString()), body);
        },
        StartupTimeout,
        "the warm-up renewal to settle"
      );
    }
  }

  public static object NewSubscription(
    string name,
    decimal price
  )
  {
    // Billing dates are today's: the renewal quote covers the period that starts now.
    return new
    {
      serviceName = name, price, startDate = DateTime.UtcNow.ToString("yyyy-MM-dd"), trialEndsAt = (string?)null
    };
  }

  public async Task<NpgsqlConnection> OpenApiDbAsync()
  {
    var connection = new NpgsqlConnection(ApiConnectionString);
    await connection.OpenAsync();
    return connection;
  }

  private async Task<string> CreateDatabaseAsync(
    string name
  )
  {
    await using (var admin = new NpgsqlConnection(_postgres.GetConnectionString()))
    {
      await admin.OpenAsync();
      await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
      await create.ExecuteNonQueryAsync();
    }

    return new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
      { Database = name, MaxPoolSize = 20 }.ConnectionString;
  }
}

/// <summary>
/// Sends the current trace context the way the real HTTP stack does: <c>SocketsHttpHandler</c> injects
/// <c>traceparent</c>, the in-memory test server handler does not, which would cut the trace at the Relay's call to the API.
/// </summary>
internal sealed class TraceparentHandler(
  HttpMessageHandler inner
) : DelegatingHandler(inner)
{
  protected override Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request,
    CancellationToken ct
  )
  {
    if (Activity.Current?.Id is { } id && !request.Headers.Contains("traceparent"))
      request.Headers.Add("traceparent", id);

    return base.SendAsync(request, ct);
  }
}

/// <summary>Every test uses the one system and runs sequentially: the services, the broker and the databases are shared.</summary>
[CollectionDefinition(Name)]
public sealed class SystemCollection : ICollectionFixture<SystemFixture>
{
  public const string Name = "system";
}