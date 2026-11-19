using JasperFx;
using JasperFx.Core;
using JasperFx.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using RabbitMQ.Client;
using SubscriptionManagement.Contracts;
using SubscriptionManagement.Relay.Features.Outbox;
using SubscriptionManagement.Relay.Infrastructure;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.Kafka;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;

namespace SubscriptionManagement.Relay;

/// <summary>Composition root of the Relay service, shared by Program.cs and the tests.</summary>
public static class ServiceSetup
{
    public const string ServiceName = "relay";

    /// <summary>ActivitySource of the outbox drain: one span per relayed row, parented on the row's traceparent.</summary>
    public const string ActivitySourceName = "SubscriptionManagement.Relay";

    public static IHostApplicationBuilder AddRelay(
        this IHostApplicationBuilder builder
    )
    {
        var connectionString = builder.Configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");
        }

        // One pool for EF Core and Wolverine's message store: the AppHost budgets a single pool per service.
        var dataSource = NpgsqlDataSource.Create(connectionString);
        builder.Services.AddSingleton(dataSource);
        builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

        // Read and checked once, at startup; the outbox drain takes it as a plain singleton.
        var options = builder.Configuration.GetSection(RelayOptions.SectionName).Get<RelayOptions>()
            ?? new RelayOptions();
        if (options.PollIntervalMs <= 0 || options.BatchSize <= 0)
        {
            throw new InvalidOperationException("Relay:PollIntervalMs and Relay:BatchSize must be positive.");
        }

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(TimeProvider.System);

        if (!Uri.TryCreate(builder.Configuration["Api:BaseUrl"], UriKind.Absolute, out var apiBaseUrl))
        {
            throw new InvalidOperationException("Api:BaseUrl is not configured.");
        }

        builder.Services.AddHttpClient<ApiClient>(client => client.BaseAddress = apiBaseUrl);

        // The consumer span becomes a child of the publisher span, which is what Tempo pairs into a messaging edge.
        RabbitMQActivitySource.TracingOptions.UsePublisherAsParent = true;

        // Off without ConnectionStrings:kafka: the Relay then only feeds RabbitMQ.
        var kafkaEnabled =
            !string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString(KafkaTopics.ConnectionName));

        builder.Services.AddWolverine(
            opts =>
            {
                opts.ServiceName = ServiceName;
                // Handlers are found in this assembly, also when a test host is the entry assembly.
                opts.ApplicationAssembly = typeof(ServiceSetup).Assembly;

                // Inbox/outbox tables in the "relay" schema of the API's database: the only schema this service owns there.
                opts.PersistMessagesWithPostgresql(dataSource, "relay");
                opts.Services.AddDbContextWithWolverineIntegration<RelayDbContext>(x => x.UseNpgsql(dataSource));
                // Wolverine registers the context as a plain scoped service (a new instance per message or drain batch)
                // and has no pooled variant. AddDbContextPool only adds what is missing: once Wolverine's registration
                // of the context is gone, the pool is built on the options Wolverine just registered (its model
                // customizer, batching and the shared data source), so the lambda is never called.
                opts.Services.RemoveAll<RelayDbContext>();
                opts.Services.AddDbContextPool<RelayDbContext>(_ => { });
                opts.UseEntityFrameworkCoreTransactions();
                opts.Policies.AutoApplyTransactions();
                opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
                opts.Policies.UseDurableInboxOnAllListeners();

                // IHttpClientFactory registers the typed client as a factory lambda, which Wolverine's generated handler
                // code can only resolve from the container: allowed for this one type.
                opts.CodeGeneration.AlwaysUseServiceLocationFor<ApiClient>();

                // ConnectionStrings:messaging. One fanout exchange and one queue per message type, named after the type.
                opts.UseRabbitMqUsingNamedConnection("messaging").AutoProvision().UseConventionalRouting();

                if (kafkaEnabled)
                {
                    // ConnectionStrings:kafka. The facts also go to the event stream that Reporting reads. A message
                    // type with an explicit rule is routed only by its explicit rules, so its RabbitMQ route is restated.
                    opts.UseKafkaUsingNamedConnection(KafkaTopics.ConnectionName).AutoProvision();
                    foreach (var fact in new[] { typeof(InvoiceIssued), typeof(SubscriptionCanceled) })
                    {
                        opts.PublishMessage(fact)
                            .ToRabbitExchange(fact.FullName!, exchange => exchange.BindQueue(fact.FullName!));
                        opts.PublishMessage(fact).ToKafkaTopic(KafkaTopics.SubscriptionEvents);
                    }
                }

                // The envelope tables and the broker objects are created at startup.
                opts.Services.AddResourceSetupOnStartup();

                opts.OnException<ConcurrencyException>()
                    .RetryWithCooldown(50.Milliseconds(), 250.Milliseconds(), 1.Seconds());
                // Database or API outage: retry in place, then reschedule; after that the envelope is dead-lettered.
                opts.OnException<NpgsqlException>()
                    .Or<HttpRequestException>()
                    .Or<TaskCanceledException>()
                    .RetryWithCooldown(50.Milliseconds(), 250.Milliseconds(), 1.Seconds())
                    .Then.ScheduleRetry(5.Seconds(), 30.Seconds());
            }
        );

        // After Wolverine, so its runtime is up before the first batch is published.
        builder.Services.AddHostedService<OutboxDrain>();

        return builder;
    }

    /// <summary><c>/health/live</c>: process is up. <c>/health/ready</c>: the database answers.</summary>
    public static void MapRelay(
        this IEndpointRouteBuilder app
    )
    {
        app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new() { Predicate = r => r.Tags.Contains("ready") });
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