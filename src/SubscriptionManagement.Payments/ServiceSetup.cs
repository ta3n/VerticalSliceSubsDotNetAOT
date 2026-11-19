using JasperFx;
using JasperFx.Core;
using JasperFx.Events.Projections;
using JasperFx.OpenTelemetry;
using JasperFx.Resources;
using Marten;
using Marten.Exceptions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using RabbitMQ.Client;
using SubscriptionManagement.Contracts;
using SubscriptionManagement.Payments.Features.Payments;
using SubscriptionManagement.Payments.Features.Payments.GetPayment;
using SubscriptionManagement.Payments.Features.Payments.GetPaymentEvents;
using SubscriptionManagement.Payments.Infrastructure;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Kafka;
using Wolverine.Marten;
using Wolverine.RabbitMQ;

namespace SubscriptionManagement.Payments;

/// <summary>Composition root of the Payments service, shared by Program.cs and the tests.</summary>
public static class ServiceSetup
{
    public const string ServiceName = "payments";

    public static IHostApplicationBuilder AddPayments(
        this IHostApplicationBuilder builder
    )
    {
        var connectionString = builder.Configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");
        }

        // One pool for Marten and Wolverine's message store: the AppHost budgets a single pool per service.
        var dataSource = NpgsqlDataSource.Create(connectionString);
        builder.Services.AddSingleton(dataSource);
        builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

        // A plain singleton, not IOptions: handlers take it as a parameter and Wolverine forbids service location.
        builder.Services.AddSingleton(
            builder.Configuration.GetSection(GatewayOptions.SectionName).Get<GatewayOptions>() ?? new GatewayOptions()
        );

        // The consumer span becomes a child of the publisher span, which is what Tempo pairs into a messaging edge.
        RabbitMQActivitySource.TracingOptions.UsePublisherAsParent = true;

        // Events, snapshots and Wolverine's inbox/outbox tables all live in the "payments" schema, so a handler's
        // appended events and its outgoing messages commit together.
        builder.Services.AddMarten(
                o =>
                {
                    o.Connection(dataSource);
                    o.DatabaseSchemaName = "payments";
                    // The Payment document is rebuilt in the transaction that appends to its stream (no async daemon).
                    o.Projections.Snapshot<Payment>(SnapshotLifecycle.Inline);
                    // SubscriptionCanceled looks payments up by subscription.
                    o.Schema.For<Payment>().Index(x => x.SubscriptionId);
                    // Feeds the "Marten" source and meter that Program.cs exports: a span per session, appends per event type.
                    o.OpenTelemetry.TrackConnections = TrackLevel.Normal;
                    o.OpenTelemetry.TrackEventCounters();
                }
            )
            .UseLightweightSessions()
            .IntegrateWithWolverine();

        // Off without ConnectionStrings:kafka: Payments then only talks RabbitMQ.
        var kafkaEnabled =
            !string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString(KafkaTopics.ConnectionName));

        builder.Services.AddWolverine(
            opts =>
            {
                opts.ServiceName = ServiceName;
                // Handlers are found in this assembly, also when a test host is the entry assembly.
                opts.ApplicationAssembly = typeof(ServiceSetup).Assembly;

                opts.Policies.AutoApplyTransactions();
                opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
                opts.Policies.UseDurableInboxOnAllListeners();

                // ConnectionStrings:messaging. One fanout exchange and one queue per message type, named after the type.
                opts.UseRabbitMqUsingNamedConnection("messaging").AutoProvision().UseConventionalRouting();

                // These replies are returned as IEnumerable<object>, so conventional routing discovers them only on send.
                // Bind their durable queues now, even when Billing has never started on this broker.
                opts.PublishMessage<PaymentSucceeded>()
                    .ToRabbitExchange(
                        typeof(PaymentSucceeded).FullName!,
                        exchange => exchange.BindQueue(typeof(PaymentSucceeded).FullName!)
                    );
                opts.PublishMessage<PaymentFailed>()
                    .ToRabbitExchange(
                        typeof(PaymentFailed).FullName!,
                        exchange => exchange.BindQueue(typeof(PaymentFailed).FullName!)
                    );
                opts.PublishMessage<PaymentRefunded>()
                    .ToRabbitExchange(
                        typeof(PaymentRefunded).FullName!,
                        exchange => exchange.BindQueue(typeof(PaymentRefunded).FullName!)
                    );

                if (kafkaEnabled)
                {
                    // ConnectionStrings:kafka. The payment outcomes also go to the event stream that Reporting reads;
                    // their RabbitMQ routes above are explicit rules too, so both sets apply.
                    opts.UseKafkaUsingNamedConnection(KafkaTopics.ConnectionName).AutoProvision();
                    opts.PublishMessage<PaymentSucceeded>().ToKafkaTopic(KafkaTopics.SubscriptionEvents);
                    opts.PublishMessage<PaymentFailed>().ToKafkaTopic(KafkaTopics.SubscriptionEvents);
                    opts.PublishMessage<PaymentRefunded>().ToKafkaTopic(KafkaTopics.SubscriptionEvents);
                }

                // The Marten schema, the envelope tables and the broker objects are created at startup.
                opts.Services.AddResourceSetupOnStartup();

                // Two messages racing on one stream (a duplicate, or a cancel against its charge): the loser of the version
                // check, or of the insert when both start the stream, runs again and decides on what the winner stored.
                opts.OnException<ConcurrencyException>()
                    .Or<ExistingStreamIdCollisionException>()
                    .RetryWithCooldown(50.Milliseconds(), 250.Milliseconds(), 1.Seconds());
                // Database outage: retry in place, then reschedule; after that the envelope is dead-lettered.
                opts.OnException<NpgsqlException>()
                    .Or<HttpRequestException>()
                    .RetryWithCooldown(50.Milliseconds(), 250.Milliseconds(), 1.Seconds())
                    .Then.ScheduleRetry(5.Seconds(), 30.Seconds());
            }
        );

        return builder;
    }

    /// <summary><c>/health/live</c>: process is up. <c>/health/ready</c>: the database answers. Plus the read endpoints.</summary>
    public static void MapPayments(
        this IEndpointRouteBuilder app
    )
    {
        app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new() { Predicate = r => r.Tags.Contains("ready") }).AllowAnonymous();

        GetPaymentEndpoint.Map(app);
        GetPaymentEventsEndpoint.Map(app);
    }
}

public sealed class DatabaseHealthCheck(
    NpgsqlDataSource dataSource
) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken ct = default
    )
    {
        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("database unreachable", ex);
        }
    }
}