using Aspire.Hosting.Dotnet;

namespace AppHost;

/// <summary>
/// The k6 scenarios of <c>scripts/*-test.js</c> as Aspire resources (Community Toolkit k6 integration). The scripts
/// are the single source of truth: the same files run from the host (<c>scripts/k6-test.sh</c>), against compose and
/// against the Kubernetes stack.
/// <para>
/// A test does not start with the app: press ▶ on <c>k6-&lt;name&gt;</c> in the dashboard (again for every run), or
/// pass <c>--LoadTest:AutoStart=smoke</c> (comma-separated names). Knobs documented in each script's header are set
/// under <c>LoadTest:&lt;name&gt;</c> in <c>appsettings.json</c> (sizes that differ from the script defaults live
/// there) or with <c>--LoadTest:&lt;name&gt;:&lt;KNOB&gt;=value</c>, e.g. <c>--LoadTest:spike:SPIKE_VUS=800</c>. A resource
/// that ends in a non-zero exit code failed its thresholds (99) — for <c>breakpoint</c> that is the expected end.
/// </para>
/// </summary>
internal static class LoadTests
{
  private const string ScriptsDir = "../../scripts";

  // No `url`/`name`: they carry entity ids and would create a Prometheus series per request. The scripts tag every
  // request with the route template (`endpoint`) instead.
  private const string K6SystemTags = "scenario,status,method,expected_response,check,error_code,group";

  /// <summary>Name → what it answers.</summary>
  private static readonly (string Name, string Purpose)[] Suite =
  [
    ("smoke", "every endpoint + error contract, a few iterations"),
    ("load", "per-endpoint load: plans, cached reads, creates, lifecycle, search, burst"),
    ("stress", "one constant-VU step of the mixed workload"),
    ("spike", "steady users + two sudden bursts, and the recovery in between"),
    ("soak", "constant request rate over a long time (leaks, GC, pools)"),
    ("rampup", "VUs grow linearly from 50 to 2000, hold, then leave"),
    ("breakpoint", "ramp the request rate until thresholds abort the run"),
    ("contention", "races on the same row: exactly one winner"),
    ("pagination", "keyset paging under writes: no duplicates, stable order, flat latency"),
    ("saga", "s2s: renewal saga (success, decline, timeout) + cancel/refund choreography"),
    ("mq", "message broker: ~5000 messages per RabbitMQ queue through the renewal and cancellation flows"),
    ("endpoints", "every route of the API, Billing and Payments in parallel flows, a coverage threshold per route"),
    (
      "bigdata",
      "big dataset in phases (seed, renew, cancel, read): Postgres, the saga, Kafka into Reporting; BIGDATA_PROFILE small|medium|large"
    )
  ];

  public static void AddK6Suite(
    this IDistributedApplicationBuilder builder,
    IResourceBuilder<DotnetProjectResource> api,
    ObservabilityStack? stack,
    S2SServices services
  )
  {
    var settings = builder.Configuration.GetSection("LoadTest");
    var autoStart = (settings["AutoStart"] ?? "")
      .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

    // Authentication: every scenario sends a bearer token of the Keycloak `loadtest` client (scripts/lib/api.js) when
    // Keycloak is on. The issuer is fixed (http://localhost:8081), so the container can ask for it at the host address.
    var keycloakEnabled =
      bool.TryParse(builder.Configuration["Keycloak:Enabled"], out var on) && on; // missing = off
    // Reporting only exists with Kafka:Enabled; the bigdata scenario then also polls it.
    var reporting = Find(builder, "reporting");

    foreach (var (name, purpose) in Suite)
    {
      // WithScript's virtualUsers/duration map to --vus/--duration, which would REPLACE the scenarios a script
      // declares; sizes therefore travel as environment variables the scripts read.
      var k6 = builder.AddK6($"k6-{name}")
        .WithBindMount(ScriptsDir, "/scripts", true)
        .WithScript($"/scripts/{name}-test.js")
        .WithArgs("--tag", $"testid={name}")
        .WithReference(api)
        .WithEnvironment("BASE_URL", api.GetEndpoint("http"))
        .WithParentRelationship(api)
        .WaitFor(api)
        .WithUrlForEndpoint(
          "http",
          url =>
          {
            url.Url = "/v1/status";
            url.DisplayText = $"k6 status ({purpose})";
            url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
          }
        );

      foreach (var knob in settings.GetSection(name).GetChildren())
        k6.WithEnvironment(knob.Key.ToUpperInvariant(), knob.Value ?? "");

      if (keycloakEnabled)
      {
        // The plain-HTTP endpoint of Identity.cs, at the address a container reaches the host by.
        var keycloak = Find(builder, "keycloak");
        if (keycloak is not null) k6.WaitFor(builder.CreateResourceBuilder(keycloak));

        k6.WithEnvironment("KEYCLOAK_URL", "http://host.docker.internal:8081")
          .WithEnvironment("KEYCLOAK_REALM", Identity.Realm)
          .WithEnvironment("KEYCLOAK_CLIENT_ID", "loadtest")
          .WithEnvironment(
            "KEYCLOAK_CLIENT_SECRET",
            "loadtest-secret"
          ); // the dev realm's client, see the realm import
      }

      if (name == "bigdata" && reporting is not null)
        k6.WithEnvironment("REPORTING_URL", builder.CreateResourceBuilder(reporting).GetEndpoint("http"))
          .WaitFor(builder.CreateResourceBuilder(reporting));

      if (name is "saga" or "endpoints" or "mq")
      {
        // The endpoints suite also drives the s2s routes; its knobs are under LoadTest:endpoints.
        if (name == "endpoints") k6.WithEnvironment("S2S", "true");

        k6.WithEnvironment("BILLING_URL", services.Billing.GetEndpoint("http"))
          .WithEnvironment("PAYMENTS_URL", services.Payments.GetEndpoint("http"))
          .WaitFor(services.Billing)
          .WaitFor(services.Payments)
          .WaitFor(services.Relay);
      }

      if (stack is not null)
        // Client-side metrics → Prometheus remote write → Grafana "Load test (k6)" dashboard.
        k6.WithEnvironment("K6_OUT", "experimental-prometheus-rw")
          .WithEnvironment(
            "K6_PROMETHEUS_RW_SERVER_URL",
            ReferenceExpression.Create($"{stack.Prometheus.GetEndpoint("http")}/api/v1/write")
          )
          .WithEnvironment("K6_PROMETHEUS_RW_TREND_STATS", "p(50),p(95),p(99),avg,max")
          .WithEnvironment("K6_PROMETHEUS_RW_PUSH_INTERVAL", "5s")
          .WithEnvironment("K6_PROMETHEUS_RW_STALE_MARKERS", "true")
          .WithEnvironment("K6_SYSTEM_TAGS", K6SystemTags)
          .WaitFor(stack.Prometheus);

      if (!autoStart.Contains(name)) k6.WithExplicitStart();
    }
  }

  /// <summary>A resource added earlier in the AppHost by name, or null: the suite does not depend on it existing.</summary>
  private static IResourceWithEndpoints? Find(
    IDistributedApplicationBuilder builder,
    string name
  )
  {
    return builder.Resources.OfType<IResourceWithEndpoints>()
      .FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
  }
}