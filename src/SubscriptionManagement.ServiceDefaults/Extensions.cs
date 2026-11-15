using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Service defaults shared by every service in the solution. Under the Aspire AppHost the OTEL_* variables are
/// injected automatically, so logs, traces and metrics show up in the Aspire dashboard; in Docker/Kubernetes the same
/// variables come from compose/Helm.
/// </summary>
public static class ServiceDefaultsExtensions
{
    /// <param name="sources">Extra ActivitySource names to export (e.g. "Wolverine"), besides ASP.NET Core, HttpClient and Npgsql.</param>
    /// <param name="meters">Extra Meter names to export; a trailing <c>*</c> is a wildcard (e.g. "Wolverine*").</param>
    public static TBuilder AddServiceDefaults<TBuilder>(
        this TBuilder builder,
        string serviceName,
        string[]? sources = null,
        string[]? meters = null
    )
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry(serviceName, sources, meters);
        return builder;
    }

    /// <summary>
    /// Telemetry is exported only when an OTLP endpoint is configured (OTEL_EXPORTER_OTLP_ENDPOINT); the exporter
    /// reads the standard OTEL_* variables itself. OTEL_SERVICE_NAME wins over <paramref name="serviceName"/>.
    /// </summary>
    public static TBuilder ConfigureOpenTelemetry<TBuilder>(
        this TBuilder builder,
        string serviceName,
        string[]? sources = null,
        string[]? meters = null
    )
        where TBuilder : IHostApplicationBuilder
    {
        if (string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])) return builder;

        var configuredName = builder.Configuration["OTEL_SERVICE_NAME"];

        builder.Logging.AddOpenTelemetry(
            o =>
            {
                o.IncludeFormattedMessage = true;
                o.IncludeScopes = true;
            }
        );

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(
                r => r.AddService(string.IsNullOrWhiteSpace(configuredName) ? serviceName : configuredName)
            )
            .WithTracing(
                t => t
                    // Health probes (Kubernetes, the Aspire dashboard) would otherwise drown out real traffic.
                    .AddAspNetCoreInstrumentation(
                        o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health")
                    )
                    // Client spans for outgoing HTTP and gRPC calls: the caller's half of a service-graph edge.
                    .AddHttpClientInstrumentation()
                    // gRPC creates a parent activity above HttpClient even when nobody subscribes to its source.
                    .AddSource("Grpc.Net.Client")
                    .AddNpgsql()
                    .AddSource(sources ?? [])
            )
            .WithMetrics(
                m => m
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddNpgsqlInstrumentation()
                    .AddMeter("System.Runtime")
                    .AddMeter(meters ?? [])
            )
            .UseOtlpExporter();

        return builder;
    }
}