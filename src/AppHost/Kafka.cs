using Aspire.Hosting.Dotnet;

namespace AppHost;

internal static class Kafka
{
  /// <summary>
  /// The event stream next to RabbitMQ: a Kafka broker (with Kafka UI), the Relay and Payments publishing copies of
  /// the business facts to the <c>subscription-events</c> topic, and Reporting reading them into
  /// a revenue read model of its own database. Opt-out with <c>Kafka:Enabled=false</c>; the services then run on
  /// RabbitMQ alone (they publish to Kafka only when they get the <c>kafka</c> connection).
  /// </summary>
  public static IResourceBuilder<DotnetProjectResource> AddEventStream(
    this IDistributedApplicationBuilder builder,
    IResourceBuilder<PostgresServerResource> postgres,
    S2SServices services,
    int servicePoolSize
  )
  {
    var kafka = builder.AddKafka("kafka")
      .WithKafkaUI(ui => ui.WithHostPort(9100))
      .WithDataVolume();
    var database = postgres.AddDatabase("reporting-db", "reporting");

    foreach (var producer in new[] { services.Payments, services.Relay })
      producer.WithReference(kafka).WaitFor(kafka);

    return builder.AddDotnetProject(
        "reporting",
        "../SubscriptionManagement.Reporting/SubscriptionManagement.Reporting.csproj"
      )
      .WithEnvironment("ConnectionStrings__Postgres", Services.ConnectionString(database, servicePoolSize))
      .WithReferenceRelationship(database)
      .WithReference(kafka)
      .WaitFor(kafka)
      .WaitFor(database)
      .WithHttpHealthCheck("/health/ready", endpointName: "http")
      .WithEnvironment("DOTNET_hostBuilder__reloadConfigOnChange", "false")
      .WithUrlForEndpoint("http", _ => new() { Url = "/reporting/revenue", DisplayText = "Revenue summary" });
  }
}