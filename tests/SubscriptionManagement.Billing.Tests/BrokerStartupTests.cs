using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using SubscriptionManagement.Contracts;

namespace SubscriptionManagement.Billing.Tests;

[Collection(BillingCollection.Name)]
public class BrokerStartupTests(
    BillingFixture fixture
)
{
    [Fact]
    public async Task Startup_binds_command_queues_before_Payments_or_Relay_have_ever_started()
    {
        await using var broker = new ContainerBuilder("rabbitmq:4.3-management")
            .WithEnvironment("RABBITMQ_DEFAULT_USER", "test")
            .WithEnvironment("RABBITMQ_DEFAULT_PASS", "test")
            .WithPortBinding(5672, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5672))
            .Build();
        await broker.StartAsync();
        var address = new Uri($"amqp://test:test@{broker.Hostname}:{broker.GetMappedPublicPort(5672)}");
        var builder =
            WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Logging:LogLevel:Default"] = "Warning",
                ["ConnectionStrings:Postgres"] = fixture.PostgresConnectionString,
                ["ConnectionStrings:messaging"] = address.ToString()
            }
        );
        builder.AddBilling();
        await using var app = builder.Build();
        await app.StartAsync();

        await using var connection = await new ConnectionFactory { Uri = address }.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        Type[] commands = [typeof(ChargePayment), typeof(CancelPayment), typeof(SettleInvoice), typeof(CancelRenewal)];
        foreach (var command in commands)
        {
            var name = command.FullName!;
            var queue = await channel.QueueDeclarePassiveAsync(name);
            Assert.Equal(0u, queue.ConsumerCount);

            // A publish reaches its queue even though the consuming services have never run.
            ReadOnlyMemory<byte> payload = new byte[] { 1, 2, 3 };
            await channel.BasicPublishAsync(name, "", true, new BasicProperties { Persistent = true }, payload);
            var stored = await channel.BasicGetAsync(name, true);
            Assert.NotNull(stored);
            Assert.Equal(payload.ToArray(), stored.Body.ToArray());
        }

        await app.StopAsync();
    }
}