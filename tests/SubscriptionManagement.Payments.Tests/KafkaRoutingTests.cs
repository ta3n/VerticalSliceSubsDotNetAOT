using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SubscriptionManagement.Contracts;
using Wolverine;

namespace SubscriptionManagement.Payments.Tests;

/// <summary>
/// With a <c>kafka</c> connection the payment outcomes go to Billing over RabbitMQ and also to the event stream that
/// Reporting reads; without one they only go to RabbitMQ.
/// </summary>
[Collection(PaymentsCollection.Name)]
public class KafkaRoutingTests(
    PaymentsFixture fixture
)
{
    private async Task<IReadOnlyList<Uri>> DestinationsAsync(
        bool kafka,
        object outcome
    )
    {
        var builder =
            WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.WebHost.UseTestServer();
        var settings = new Dictionary<string, string?>
        {
            ["Logging:LogLevel:Default"] = "Warning",
            ["ConnectionStrings:Postgres"] = fixture.PostgresConnectionString,
            ["ConnectionStrings:messaging"] = "amqp://guest:guest@127.0.0.1:1"
        };
        if (kafka)
        {
            settings["ConnectionStrings:kafka"] = "127.0.0.1:1";
        }

        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddPayments();
        builder.Services.DisableAllExternalWolverineTransports();
        await using var app = builder.Build();
        await app.StartAsync();

        using var scope = app.Services.CreateScope();
        var destinations = scope.ServiceProvider.GetRequiredService<IMessageBus>()
            .PreviewSubscriptions(outcome)
            .Select(envelope => envelope.Destination!)
            .ToArray();
        await app.StopAsync();
        return destinations;
    }

    public static TheoryData<object> Outcomes()
    {
        var invoice = Guid.NewGuid();
        return new TheoryData<object>
        {
            new PaymentSucceeded(invoice, Guid.NewGuid()),
            new PaymentFailed(invoice, Guid.NewGuid(), "declined"),
            new PaymentRefunded(Guid.NewGuid(), invoice, Guid.NewGuid(), 5m, "USD", "canceled")
        };
    }

    [Theory]
    [MemberData(nameof(Outcomes))]
    public async Task Outcomes_go_to_billing_over_rabbitmq_and_to_the_kafka_event_stream(
        object outcome
    )
    {
        var destinations = await DestinationsAsync(true, outcome);

        Assert.Contains(
            destinations,
            uri => uri.Scheme == "rabbitmq" && uri.ToString().Contains(outcome.GetType().FullName!)
        );
        Assert.Contains(
            destinations,
            uri => uri.Scheme == "kafka" && uri.ToString().Contains(KafkaTopics.SubscriptionEvents)
        );
    }

    [Fact]
    public async Task Without_a_kafka_connection_outcomes_only_go_to_rabbitmq()
    {
        var destinations = await DestinationsAsync(false, new PaymentSucceeded(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Contains(destinations, uri => uri.Scheme == "rabbitmq");
        Assert.DoesNotContain(destinations, uri => uri.Scheme == "kafka");
    }
}