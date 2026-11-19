using JasperFx;
using JasperFx.Core;
using JasperFx.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using RabbitMQ.Client;
using SubscriptionManagement.Billing.Features.CreditNotes.ListCreditNotes;
using SubscriptionManagement.Billing.Features.Renewals;
using SubscriptionManagement.Billing.Infrastructure;
using SubscriptionManagement.Contracts;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;

namespace SubscriptionManagement.Billing;

/// <summary>Composition root of the Billing service, shared by Program.cs and the tests.</summary>
public static class ServiceSetup
{
    public const string ServiceName = "billing";

    public static IHostApplicationBuilder AddBilling(
        this IHostApplicationBuilder builder
    )
    {
        var connectionString = builder.Configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");

        // One pool for EF Core and Wolverine's message store: the AppHost budgets a single pool per service.
        var dataSource = NpgsqlDataSource.Create(connectionString);
        builder.Services.AddSingleton(dataSource);
        builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

        // Registered as instances: Wolverine passes singletons straight into the handlers it generates.
        builder.Services.AddSingleton(BillingOptions.From(builder.Configuration));
        builder.Services.AddSingleton(TimeProvider.System);

        builder.Services.AddGrpc();

        // The consumer span becomes a child of the publisher span, which is what Tempo pairs into a messaging edge.
        RabbitMQActivitySource.TracingOptions.UsePublisherAsParent = true;

        builder.Services.AddWolverine(
            opts =>
            {
                opts.ServiceName = ServiceName;
                // Handlers are found in this assembly, also when a test host is the entry assembly.
                opts.ApplicationAssembly = typeof(ServiceSetup).Assembly;

                // Inbox/outbox tables in the "wolverine" schema; a handler's EF changes and its outgoing messages commit together.
                opts.PersistMessagesWithPostgresql(dataSource, "wolverine");
                opts.Services.AddDbContextWithWolverineIntegration<BillingDbContext>(x => x.UseNpgsql(dataSource));
                // Wolverine registers the context as a plain scoped service (a new instance per message or request) and has
                // no pooled variant. AddDbContextPool only adds what is missing: once Wolverine's registration of the
                // context is gone, the pool is built on the options Wolverine just registered (its model customizer,
                // batching and the shared data source), so the lambda is never called.
                opts.Services.RemoveAll<BillingDbContext>();
                opts.Services.AddDbContextPool<BillingDbContext>(_ => { });
                opts.UseEntityFrameworkCoreTransactions();
                opts.Policies.AutoApplyTransactions();
                opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
                opts.Policies.UseDurableInboxOnAllListeners();
                // RenewalTimeout is a delayed message on a local queue. Durable, it is stored with the saga and survives a
                // restart; otherwise it would only be a timer in this process.
                opts.Policies.UseDurableLocalQueues();

                // ConnectionStrings:messaging. One fanout exchange and one queue per message type, named after the type.
                // RenewalTimeout is Billing's own delayed message: it has no business on the broker.
                opts.UseRabbitMqUsingNamedConnection("messaging")
                    .AutoProvision()
                    .UseConventionalRouting(routing => routing.ExcludeTypes(type => type == typeof(RenewalTimeout)));

                // OutgoingMessages hides these types from startup discovery; bind their queues before their consumers run.
                opts.PublishMessage<ChargePayment>()
                    .ToRabbitExchange(
                        typeof(ChargePayment).FullName!,
                        exchange => exchange.BindQueue(typeof(ChargePayment).FullName!)
                    );
                opts.PublishMessage<CancelPayment>()
                    .ToRabbitExchange(
                        typeof(CancelPayment).FullName!,
                        exchange => exchange.BindQueue(typeof(CancelPayment).FullName!)
                    );
                opts.PublishMessage<SettleInvoice>()
                    .ToRabbitExchange(
                        typeof(SettleInvoice).FullName!,
                        exchange => exchange.BindQueue(typeof(SettleInvoice).FullName!)
                    );
                opts.PublishMessage<CancelRenewal>()
                    .ToRabbitExchange(
                        typeof(CancelRenewal).FullName!,
                        exchange => exchange.BindQueue(typeof(CancelRenewal).FullName!)
                    );

                // No EF migrations: the DbContext's tables, the envelope tables and the broker objects are created at startup.
                opts.UseEntityFrameworkCoreWolverineManagedMigrations();
                opts.Services.AddResourceSetupOnStartup();

                opts.OnException<ConcurrencyException>()
                    .RetryWithCooldown(50.Milliseconds(), 250.Milliseconds(), 1.Seconds());
                // Database or downstream outage: retry in place, then reschedule; after that the envelope is dead-lettered.
                opts.OnException<NpgsqlException>()
                    .Or<HttpRequestException>()
                    .Or<Exception>(WrapsTransientDatabaseFailure)
                    .RetryWithCooldown(50.Milliseconds(), 250.Milliseconds(), 1.Seconds())
                    .Then.ScheduleRetry(5.Seconds(), 30.Seconds());
            }
        );

        return builder;
    }

    // A query or save that fails inside EF Core does not surface as the NpgsqlException itself: it arrives wrapped in
    // an InvalidOperationException or a DbUpdateException. Only transient ones are worth retrying; a constraint
    // violation fails the same way every time.
    private static bool WrapsTransientDatabaseFailure(
        Exception exception
    )
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
            if (inner is NpgsqlException { IsTransient: true })
                return true;

        return false;
    }

    /// <summary>
    /// <c>/health/live</c>: process is up. <c>/health/ready</c>: the database answers. BillingService is gRPC and needs
    /// HTTP/2, so it only answers on the "grpc" endpoint.
    /// </summary>
    public static void MapBilling(
        this IEndpointRouteBuilder app
    )
    {
        app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new() { Predicate = r => r.Tags.Contains("ready") }).AllowAnonymous();

        // Between services, inside the system: the API calls it without a user token.
        app.MapGrpcService<BillingGrpcService>().AllowAnonymous();
        ListCreditNotesEndpoint.Map(app);
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