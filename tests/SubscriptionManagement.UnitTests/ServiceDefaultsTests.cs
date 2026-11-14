using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace SubscriptionManagement.UnitTests;

/// <summary>SubscriptionManagement.ServiceDefaults: telemetry is opt-in through the standard OTEL_* settings.</summary>
public class ServiceDefaultsTests
{
    private static IHost Build(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddServiceDefaults("subscription-management");
        return builder.Build();
    }

    [Fact]
    public void Without_an_otlp_endpoint_nothing_is_exported()
    {
        using var host = Build([]);

        Assert.Null(host.Services.GetService<TracerProvider>());
        Assert.Null(host.Services.GetService<MeterProvider>());
    }

    [Theory]
    [InlineData(null, "subscription-management")]
    [InlineData("subs-canary", "subs-canary")]
    public void With_an_otlp_endpoint_traces_metrics_and_logs_are_exported(string? configuredName, string expectedName)
    {
        using var host = Build(new()
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://127.0.0.1:4317",
            ["OTEL_SERVICE_NAME"] = configuredName,
        });

        var tracer = host.Services.GetRequiredService<TracerProvider>();
        Assert.NotNull(host.Services.GetService<MeterProvider>());
        Assert.Contains(host.Services.GetServices<ILoggerProvider>(), p => p.GetType().Name == "OpenTelemetryLoggerProvider");
        var serviceName = tracer.GetResource().Attributes.Single(a => a.Key == "service.name").Value;
        Assert.Equal(expectedName, serviceName);
    }
}
