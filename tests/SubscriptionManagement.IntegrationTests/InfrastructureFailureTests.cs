using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using SubscriptionManagement.Migrator;

namespace SubscriptionManagement.IntegrationTests;

/// <summary>
/// Behaviour when the platform around the API misbehaves: an unreachable or missing database, the telemetry
/// pipeline's health-probe filter, and the migrator's command-line contract (exit codes used by compose/Kubernetes).
/// </summary>
public sealed class InfrastructureFailureTests(
  ApiFixture fixture
) : ApiTestBase(fixture)
{
  // Port 1 on loopback: nothing listens there, so connects fail immediately.
  private const string UnreachablePostgres =
    "Host=127.0.0.1;Port=1;Database=subscriptions;Username=x;Password=x;Timeout=2";

  [Fact]
  public async Task An_unreachable_database_gives_an_opaque_500_and_takes_the_instance_out_of_rotation()
  {
    await using var factory = Fixture.CreateFactory(false, UnreachablePostgres);
    using var client = factory.CreateClient();

    var response = await client.GetAsync("/plans");
    var problem = await AssertProblemAsync(response, HttpStatusCode.InternalServerError, "internal", "/plans");
    Assert.DoesNotContain("127.0.0.1", problem.GetRawText()); // no connection details leak

    var ready = await client.GetAsync("/health/ready");
    Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
    Assert.Equal("DOWN", (await ReadAsync(ready)).GetProperty("status").GetString());

    var live = await client.GetAsync("/health/live"); // the process itself is fine: no restart loop
    Assert.Equal(HttpStatusCode.OK, live.StatusCode);
  }

  [Fact]
  public void A_missing_database_connection_string_fails_at_startup()
  {
    using var factory = Fixture.CreateFactory(false, "");

    var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

    Assert.Contains("ConnectionStrings:Postgres is not configured", Flatten(error));
  }

  [Fact]
  public async Task With_telemetry_enabled_health_probes_are_not_traced_but_api_requests_are()
  {
    await using var factory = Fixture.CreateFactory(
      false,
      settings: new Dictionary<string, string>
      {
        // Nothing listens there; the exporter just drops the batches. Only the recording decision matters here.
        ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://127.0.0.1:1"
      }
    );
    using var client = factory.CreateClient();
    var recorded = new Dictionary<ActivityTraceId, bool>();
    using var listener = new ActivityListener
    {
      ShouldListenTo = source => source.Name == "Microsoft.AspNetCore",
      // Leave the sampling decision to the OpenTelemetry SDK under test.
      Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.None,
      ActivityStopped = activity =>
      {
        lock (recorded)
        {
          recorded[activity.TraceId] = activity.Recorded;
        }
      }
    };
    ActivitySource.AddActivityListener(listener);

    var healthTrace = await GetWithTraceAsync(client, "/health/live");
    var apiTrace = await GetWithTraceAsync(client, "/plans");

    // The server activity stops only after the response has been handed back, so give it a moment.
    bool? healthRecorded = null, apiRecorded = null;
    for (var i = 0; i < 50 && (healthRecorded is null || apiRecorded is null); i++)
    {
      lock (recorded)
      {
        healthRecorded = recorded.TryGetValue(healthTrace, out var h) ? h : null;
        apiRecorded = recorded.TryGetValue(apiTrace, out var a) ? a : null;
      }

      await Task.Delay(100);
    }

    Assert.False(healthRecorded);
    Assert.True(apiRecorded);
  }

  [Fact]
  public async Task Migrator_exit_codes_distinguish_usage_errors_failures_and_success()
  {
    var previous = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");
    try
    {
      Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", null);
      Assert.Equal(2, RunMigrator()); // no connection string at all
      Assert.Equal(1, RunMigrator(UnreachablePostgres)); // migration failed
      Assert.Equal(0, RunMigrator(Fixture.PostgresConnectionString)); // schema up to date

      Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", Fixture.PostgresConnectionString);
      Assert.Equal(0, RunMigrator()); // from the environment (compose/Helm)
    }
    finally
    {
      Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", previous);
    }

    // The runner itself agrees that nothing is left to apply.
    Assert.Equal(0, await MigrationRunner.RunAsync(Fixture.PostgresConnectionString, NullLogger.Instance));
  }

  private static int RunMigrator(
    params string[] args
  )
  {
    return (int)typeof(MigrationRunner).Assembly.EntryPoint!.Invoke(null, [args])!;
  }

  /// <summary>Sends a request as part of a fresh W3C trace, so the server activity can be found by its trace id.</summary>
  private static async Task<ActivityTraceId> GetWithTraceAsync(
    HttpClient client,
    string path
  )
  {
    var traceId = ActivityTraceId.CreateRandom();
    using var request = new HttpRequestMessage(HttpMethod.Get, path);
    request.Headers.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");
    using var response = await client.SendAsync(request);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    return traceId;
  }

  private static string Flatten(
    Exception error
  )
  {
    return string.Join(" | ", Enumerate(error).Select(e => e.Message));
  }

  private static IEnumerable<Exception> Enumerate(
    Exception error
  )
  {
    for (var e = error; e is not null; e = e.InnerException)
    {
      yield return e;
      if (e is AggregateException aggregate)
        foreach (var inner in aggregate.InnerExceptions.SelectMany(Enumerate))
          yield return inner;
    }
  }
}