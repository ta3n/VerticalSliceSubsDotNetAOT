using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SubscriptionManagement.Contracts;
using SubscriptionManagement.Reporting.Features.Revenue;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Kafka;

namespace SubscriptionManagement.Reporting.Tests;

/// <summary>
/// One real PostgreSQL and one real Kafka (Testcontainers) shared by the whole run. The Reporting host is composed
/// like Program.cs (<see cref="ServiceSetup"/>) on an in-memory test server and reads the real broker. Events are
/// published by a second Wolverine host with the same Kafka routing the Relay and Payments use, so the messages have
/// the production wire format.
/// </summary>
public sealed class ReportingFixture : IAsyncLifetime
{
    private readonly KafkaContainer _kafka = new KafkaBuilder("confluentinc/cp-kafka:7.5.12").Build();
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private WebApplication _app = null!;
    private IHost _publisher = null!;

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _kafka.StartAsync());

        var builder =
            WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Logging:LogLevel:Default"] = "Warning",
                ["ConnectionStrings:Postgres"] = _postgres.GetConnectionString(),
                ["ConnectionStrings:kafka"] = _kafka.GetBootstrapAddress()
            }
        );
        builder.AddReporting();
        _app = builder.Build();
        _app.MapReporting();
        await _app.StartAsync();
        Client = _app.GetTestClient();

        _publisher = await Host.CreateDefaultBuilder()
            .UseWolverine(
                opts =>
                {
                    opts.UseKafka(_kafka.GetBootstrapAddress()).AutoProvision();
                    foreach (var fact in new[]
                        {
                            typeof(InvoiceIssued), typeof(SubscriptionCanceled), typeof(PaymentSucceeded),
                            typeof(PaymentFailed), typeof(PaymentRefunded)
                        })
                        opts.PublishMessage(fact).ToKafkaTopic(KafkaTopics.SubscriptionEvents);
                }
            )
            .StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _publisher.StopAsync();
        await _app.StopAsync();
        await _app.DisposeAsync();
        await _kafka.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>Publishes to the topic, as the Relay and Payments do.</summary>
    public async Task PublishAsync(
        params object[] facts
    )
    {
        var bus = _publisher.Services.GetRequiredService<IMessageBus>();
        foreach (var fact in facts) await bus.PublishAsync(fact);
    }

    /// <summary>Polls the summary until <paramref name="done" /> holds; the consumer is asynchronous.</summary>
    public async Task<RevenueSummary> WaitForAsync(
        Func<RevenueSummary, bool> done,
        TimeSpan? timeout = null
    )
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(60));
        RevenueSummary? last = null;
        while (DateTime.UtcNow < deadline)
        {
            last = await Client.GetFromJsonAsync<RevenueSummary>("/reporting/revenue");
            if (last is not null && done(last)) return last;
            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"The summary never satisfied the condition; last: {JsonSerializer.Serialize(last)}"
        );
    }
}