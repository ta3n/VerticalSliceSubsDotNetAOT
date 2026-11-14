using Aspire.Hosting.ApplicationModel;

namespace AppHost;

/// <summary>
/// Monitoring stack for local load testing, next to the Aspire dashboard:
/// OpenTelemetry Collector → Prometheus (metrics) / Tempo (traces) / Loki (logs), PostgreSQL + Redis exporters, and
/// Grafana with the datasources and dashboards provisioned from <c>observability/</c>.
/// </summary>
internal static class Observability
{
    // Fixed host ports so bookmarks and the k6 scripts on the host keep working (9090 is often taken).
    public const int GrafanaPort = 3000;
    public const int PrometheusPort = 9190;

    private const string ConfigDir = "observability";

    public static ObservabilityStack AddObservabilityStack(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<PostgresServerResource> postgres,
        IResourceBuilder<RedisResource> redis)
    {
        var prometheus = builder.AddContainer("prometheus", "prom/prometheus", "v3.16.0")
            .WithBindMount($"{ConfigDir}/prometheus.yml", "/etc/prometheus/prometheus.yml", isReadOnly: true)
            .WithArgs(
                "--config.file=/etc/prometheus/prometheus.yml",
                "--storage.tsdb.path=/prometheus",
                "--storage.tsdb.retention.time=7d",
                "--web.enable-otlp-receiver",
                "--web.enable-remote-write-receiver",
                "--enable-feature=exemplar-storage")
            .WithHttpEndpoint(port: PrometheusPort, targetPort: 9090)
            .WithHttpHealthCheck("/-/ready")
            .WithIconName("ChartMultiple");
        var prometheusHttp = prometheus.GetEndpoint("http");

        var tempo = builder.AddContainer("tempo", "grafana/tempo", "2.10.10")
            .WithBindMount($"{ConfigDir}/tempo.yaml", "/etc/tempo/tempo.yaml", isReadOnly: true)
            .WithArgs("-config.file=/etc/tempo/tempo.yaml", "-config.expand-env=true")
            .WithEnvironment("PROMETHEUS_REMOTE_WRITE_URL", ReferenceExpression.Create($"{prometheusHttp}/api/v1/write"))
            .WithHttpEndpoint(targetPort: 3200)
            .WithEndpoint(targetPort: 4317, name: "otlp-grpc", scheme: "http")
            .WithHttpHealthCheck("/ready")
            .WaitFor(prometheus)
            .WithIconName("BranchFork");

        var loki = builder.AddContainer("loki", "grafana/loki", "3.7.10")
            .WithBindMount($"{ConfigDir}/loki.yaml", "/etc/loki/loki.yaml", isReadOnly: true)
            .WithArgs("-config.file=/etc/loki/loki.yaml")
            .WithHttpEndpoint(targetPort: 3100)
            .WithHttpHealthCheck("/ready")
            .WithIconName("DocumentText");

        // Receivers stay on plain HTTP even under the https profile: the .NET exporters would otherwise need a
        // trusted dev certificate. AppForwarding points every resource's OTLP exporter at the collector.
        var collector = builder.AddOpenTelemetryCollector("otel-collector", s =>
            {
                s.CollectorTag = "0.165.0";
                s.ForceNonSecureReceiver = true;
            })
            .WithConfig($"{ConfigDir}/otel-collector.yaml")
            .WithAppForwarding()
            .WithEnvironment("ASPIRE_OTLP_INSECURE", DashboardOtlpIsPlainHttp(builder) ? "true" : "false")
            .WithEnvironment("PROMETHEUS_ENDPOINT", prometheusHttp)
            .WithEnvironment("TEMPO_OTLP_ENDPOINT", tempo.GetEndpoint("otlp-grpc"))
            .WithEnvironment("LOKI_ENDPOINT", loki.GetEndpoint("http"))
            .WithEndpoint(targetPort: 8888, name: "metrics", scheme: "http")
            .WaitFor(prometheus)
            .WaitFor(tempo)
            .WaitFor(loki);

        // Server-side PostgreSQL / Redis metrics (connections, TPS, locks, cache hits, memory ...), scraped by Prometheus.
        builder.AddContainer("postgres-exporter", "prometheuscommunity/postgres-exporter", "v0.20.1")
            .WithEnvironment("DATA_SOURCE_NAME", ReferenceExpression.Create($"{postgres.Resource.UriExpression}/postgres?sslmode=disable"))
            .WithHttpEndpoint(targetPort: 9187)
            .WithHttpHealthCheck("/metrics")
            .WithParentRelationship(postgres)
            .WaitFor(postgres);

        builder.AddContainer("redis-exporter", "oliver006/redis_exporter", "v1.97.0")
            .WithEnvironment("REDIS_ADDR", redis.Resource.UriExpression)
            .WithHttpEndpoint(targetPort: 9121)
            .WithHttpHealthCheck("/metrics")
            .WithParentRelationship(redis)
            .WaitFor(redis);

        // Image renderer (headless Chromium) as a separate service: Grafana's "Export as image" / the /render API and
        // alert screenshots call it, and it calls back into Grafana to load the panel. Both sides share a token that
        // is generated per run.
        var rendererToken = builder.AddParameter(
            "grafana-renderer-token", new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true);
        var renderer = builder.AddContainer("grafana-renderer", "grafana/grafana-image-renderer", "v5.12.11")
            .WithEnvironment("AUTH_TOKEN", rendererToken)
            .WithEnvironment("API_SILENCE_REQUEST_LOG_PATH", "/healthz")
            .WithHttpEndpoint(targetPort: 8081)
            .WithHttpHealthCheck("/healthz")
            .WithIconName("Image");

        var grafana = builder.AddContainer("grafana", "grafana/grafana", "13.2.6")
            .WithBindMount($"{ConfigDir}/grafana/provisioning", "/etc/grafana/provisioning", isReadOnly: true)
            .WithBindMount($"{ConfigDir}/grafana/dashboards", "/var/lib/grafana/dashboards", isReadOnly: true)
            .WithEnvironment("PROMETHEUS_URL", prometheusHttp)
            .WithEnvironment("TEMPO_URL", tempo.GetEndpoint("http"))
            .WithEnvironment("LOKI_URL", loki.GetEndpoint("http"))
            // Local tool: anonymous viewing (+ Explore); sign in as admin/admin to edit dashboards.
            .WithEnvironment("GF_AUTH_ANONYMOUS_ENABLED", "true")
            .WithEnvironment("GF_USERS_VIEWERS_CAN_EDIT", "true")
            .WithEnvironment("GF_SECURITY_ADMIN_USER", "admin")
            .WithEnvironment("GF_SECURITY_ADMIN_PASSWORD", "admin")
            .WithEnvironment("GF_ANALYTICS_REPORTING_ENABLED", "false")
            .WithEnvironment("GF_NEWS_NEWS_FEED_ENABLED", "false")
            .WithEnvironment("GF_DASHBOARDS_DEFAULT_HOME_DASHBOARD_PATH", "/var/lib/grafana/dashboards/service-overview.json")
            .WithEnvironment("GF_RENDERING_SERVER_URL", ReferenceExpression.Create($"{renderer.GetEndpoint("http")}/render"))
            .WithEnvironment("GF_RENDERING_RENDERER_TOKEN", rendererToken)
            .WithHttpEndpoint(port: GrafanaPort, targetPort: 3000)
            .WithHttpHealthCheck("/api/health")
            .WithUrlForEndpoint("http", u => u.DisplayText = "Grafana")
            .WaitFor(prometheus)
            .WaitFor(tempo)
            .WaitFor(loki)
            .WaitFor(renderer)
            .WithIconName("DataArea");
        // The address the renderer uses to reach Grafana (container network), not the host URL.
        grafana.WithEnvironment("GF_RENDERING_CALLBACK_URL", ReferenceExpression.Create($"{grafana.GetEndpoint("http")}/"));
        renderer.WithParentRelationship(grafana);

        return new ObservabilityStack(collector, prometheus, grafana);
    }

    private static bool DashboardOtlpIsPlainHttp(IDistributedApplicationBuilder builder) =>
        builder.Configuration["ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL"]?.StartsWith("http://", StringComparison.OrdinalIgnoreCase) == true;
}

internal sealed record ObservabilityStack(
    IResourceBuilder<OpenTelemetryCollectorResource> Collector,
    IResourceBuilder<ContainerResource> Prometheus,
    IResourceBuilder<ContainerResource> Grafana);
