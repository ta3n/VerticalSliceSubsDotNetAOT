using Confluent.Kafka;
using JasperFx.Resources;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using SubscriptionManagement.Contracts;
using SubscriptionManagement.Reporting.Features.Revenue;
using Wolverine;
using Wolverine.Kafka;

namespace SubscriptionManagement.Reporting;

/// <summary>
/// Composition root of the Reporting service, shared by Program.cs and the tests. Reads the
/// <see cref="KafkaTopics.SubscriptionEvents" /> topic into a revenue read model in its own database and serves it.
/// </summary>
public static class ServiceSetup
{
    public const string ServiceName = "reporting";

    /// <summary>The consumer group: the topic's offsets are tracked per group, so a new group reads from the start.</summary>
    public const string ConsumerGroup = "reporting";

    public static IHostApplicationBuilder AddReporting(
        this IHostApplicationBuilder builder
    )
    {
        var connectionString = builder.Configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");
        }

        var dataSource = NpgsqlDataSource.Create(connectionString);
        builder.Services.AddSingleton(dataSource);
        builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

        // Before Wolverine: the tables must exist when the first message arrives.
        builder.Services.AddHostedService<RevenueSchemaInitializer>();

        builder.Services.AddWolverine(
            opts =>
            {
                opts.ServiceName = ServiceName;
                // Handlers are found in this assembly, also when a test host is the entry assembly.
                opts.ApplicationAssembly = typeof(ServiceSetup).Assembly;

                // ConnectionStrings:kafka. AutoProvision creates the topic at startup.
                opts.UseKafkaUsingNamedConnection(KafkaTopics.ConnectionName).AutoProvision();

                // Inline: the offset is committed after the handler has written its row, so a crash re-delivers the
                // message (the handlers are idempotent). A new group starts from the first message of the topic.
                opts.ListenToKafkaTopic(KafkaTopics.SubscriptionEvents)
                    .ProcessInline()
                    .ConfigureConsumer(
                        consumer =>
                        {
                            consumer.GroupId = ConsumerGroup;
                            consumer.AutoOffsetReset = AutoOffsetReset.Earliest;
                        }
                    );

                opts.Services.AddResourceSetupOnStartup();
            }
        );

        return builder;
    }

    /// <summary><c>/health/live</c>: process is up. <c>/health/ready</c>: the database answers. Plus the read endpoint.</summary>
    public static void MapReporting(
        this IEndpointRouteBuilder app
    )
    {
        app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new() { Predicate = r => r.Tags.Contains("ready") }).AllowAnonymous();

        GetRevenueSummaryEndpoint.Map(app);
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