using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.UnitTests;

/// <summary>These tests change ASPNETCORE_HTTP_PORTS, so they must not run next to anything that reads it.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection
{
    public const string Name = "process-environment";
}

/// <summary>
/// The `SubscriptionManagement healthcheck [url]` mode used as the container HEALTHCHECK (the chiseled image has no
/// curl), against a real Kestrel listener.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class HealthProbeTests : IAsyncLifetime
{
    private WebApplication _server = null!;
    private Uri _baseUri = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _server = builder.Build();
        _server.MapGet("/health/live", () => Results.Text("{\"status\":\"UP\"}", "application/json"));
        _server.MapGet("/down", () => Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
        await _server.StartAsync();
        _baseUri = new Uri(_server.Urls.First());
    }

    public async Task DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task Healthy_endpoint_exits_0() =>
        Assert.Equal(0, await HealthProbe.RunAsync(["healthcheck", new Uri(_baseUri, "/health/live").ToString()]));

    [Fact]
    public async Task Unhealthy_status_exits_1() =>
        Assert.Equal(1, await HealthProbe.RunAsync(["healthcheck", new Uri(_baseUri, "/down").ToString()]));

    [Fact]
    public async Task Unreachable_endpoint_exits_1()
    {
        var closedPort = await ClosedPortAsync();
        Assert.Equal(1, await HealthProbe.RunAsync(["healthcheck", $"http://127.0.0.1:{closedPort}/health/live"]));
    }

    [Fact]
    public async Task Without_a_url_the_first_ASPNETCORE_HTTP_PORTS_entry_is_probed()
    {
        var previous = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS");
        try
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_HTTP_PORTS", $"{_baseUri.Port};{await ClosedPortAsync()}");
            Assert.Equal(0, await HealthProbe.RunAsync(["healthcheck"]));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_HTTP_PORTS", previous);
        }
    }

    [Fact]
    public void The_api_entry_point_dispatches_the_healthcheck_argument()
    {
        var entryPoint = typeof(HealthProbe).Assembly.EntryPoint!;

        var exitCode = entryPoint.Invoke(null, [new[] { "healthcheck", new Uri(_baseUri, "/down").ToString() }]);

        Assert.Equal(1, exitCode);
    }

    /// <summary>A port that was just bound and released, so nothing listens on it.</summary>
    private static async Task<int> ClosedPortAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        await app.StartAsync();
        var port = new Uri(app.Urls.First()).Port;
        await app.StopAsync();
        return port;
    }
}
