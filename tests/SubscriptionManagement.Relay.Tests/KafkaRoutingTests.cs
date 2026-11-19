using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SubscriptionManagement.Contracts;
using SubscriptionManagement.Relay.Features.Outbox;
using Wolverine;

namespace SubscriptionManagement.Relay.Tests;

/// <summary>
/// With a <c>kafka</c> connection the Relay sends each fact to RabbitMQ and to the event stream. An explicit Kafka rule
/// replaces Wolverine's conventional RabbitMQ route of the type, so the test pins that the RabbitMQ one survives.
/// </summary>
[Collection(RelayCollection.Name)]
public class KafkaRoutingTests(
    RelayFixture fixture
)
{
    private async Task<IReadOnlyList<Uri>> DestinationsAsync(
        bool kafka,
        object fact
    )
    {
        var builder =
            WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.WebHost.UseTestServer();
        var settings = new Dictionary<string, string?>
        {
            ["Logging:LogLevel:Default"] = "Warning",
            ["ConnectionStrings:Postgres"] = fixture.PostgresConnectionString,
            ["ConnectionStrings:messaging"] = "amqp://guest:guest@127.0.0.1:1",
            ["Api:BaseUrl"] = "https://api.test"
        };
        if (kafka)
        {
            settings["ConnectionStrings:kafka"] = "127.0.0.1:1";
        }

        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddRelay();
        builder.Services.DisableAllExternalWolverineTransports();
        builder.Services.Remove(builder.Services.Single(d => d.ImplementationType == typeof(OutboxDrain)));
        await using var app = builder.Build();
        await app.StartAsync();

        using var scope = app.Services.CreateScope();
        var destinations = scope.ServiceProvider.GetRequiredService<IMessageBus>()
            .PreviewSubscriptions(fact)
            .Select(envelope => envelope.Destination!)
            .ToArray();
        await app.StopAsync();
        return destinations;
    }

    private static InvoiceIssued Invoice()
    {
        return new InvoiceIssued(
            Guid.NewGuid(),
            Guid.NewGuid(),
            10m,
            "USD",
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 2, 1)
        );
    }

    [Fact]
    public async Task Facts_go_to_rabbitmq_and_to_the_kafka_event_stream()
    {
        var invoice = await DestinationsAsync(true, Invoice());
        Assert.Contains(
            invoice,
            uri => uri.Scheme == "rabbitmq" && uri.ToString().Contains(typeof(InvoiceIssued).FullName!)
        );
        Assert.Contains(
            invoice,
            uri => uri.Scheme == "kafka" && uri.ToString().Contains(KafkaTopics.SubscriptionEvents)
        );

        var canceled = await DestinationsAsync(
            true,
            new SubscriptionCanceled(Guid.NewGuid(), new DateOnly(2026, 1, 1))
        );
        Assert.Contains(
            canceled,
            uri => uri.Scheme == "rabbitmq" && uri.ToString().Contains(typeof(SubscriptionCanceled).FullName!)
        );
        Assert.Contains(
            canceled,
            uri => uri.Scheme == "kafka" && uri.ToString().Contains(KafkaTopics.SubscriptionEvents)
        );
    }

    [Fact]
    public async Task Without_a_kafka_connection_facts_only_go_to_rabbitmq()
    {
        var destinations = await DestinationsAsync(false, Invoice());

        Assert.Contains(destinations, uri => uri.Scheme == "rabbitmq");
        Assert.DoesNotContain(destinations, uri => uri.Scheme == "kafka");
    }
}