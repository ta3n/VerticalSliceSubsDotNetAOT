using Aspire.Hosting.Dotnet;

namespace AppHost;

internal sealed record S2SServices(
  IResourceBuilder<RabbitMQServerResource> Messaging,
  IResourceBuilder<DotnetProjectResource> Billing,
  IResourceBuilder<DotnetProjectResource> Payments,
  IResourceBuilder<DotnetProjectResource> Relay
);

internal static class Services
{
  public static S2SServices AddS2SServices(
    this IDistributedApplicationBuilder builder,
    IResourceBuilder<PostgresServerResource> postgres,
    IResourceBuilder<PostgresDatabaseResource> database,
    IResourceBuilder<DotnetProjectResource> migrator,
    IResourceBuilder<DotnetProjectResource> api,
    int servicePoolSize
  )
  {
    var messaging = builder.AddRabbitMQ("messaging")
      .WithImageTag("4.3-management")
      .WithManagementPlugin()
      .WithDataVolume();
    var billingDb = postgres.AddDatabase("billing-db", "billing");
    var paymentsDb = postgres.AddDatabase("payments-db", "payments");

    // Every service runs Replicas instances; Aspire's endpoint proxy balances HTTP/gRPC across them. Billing and
    // Payments (Wolverine / Marten on PostgreSQL) coordinate through the database; the Relay's outbox claim uses
    // FOR UPDATE SKIP LOCKED, so instances take different rows.
    var replicas = builder.Configuration.Required<int>("Replicas");

    var billing = builder.AddDotnetProject(
        "billing",
        "../SubscriptionManagement.Billing/SubscriptionManagement.Billing.csproj"
      )
      .WithEnvironment("ConnectionStrings__Postgres", ConnectionString(billingDb, servicePoolSize))
      .WithReferenceRelationship(billingDb)
      .WithReference(messaging)
      .WaitFor(billingDb)
      .WaitFor(messaging)
      .WithHttpHealthCheck("/health/ready", endpointName: "http")
      .WithReplicas(replicas)
      .WithEnvironment("DOTNET_hostBuilder__reloadConfigOnChange", "false")
      .WithEnvironment(
        "Billing__RenewalTimeoutSeconds",
        builder.Configuration.Required<string>("Billing:RenewalTimeoutSeconds")
      )
      .WithEnvironment("Billing__TaxRate", builder.Configuration.Required<string>("Billing:TaxRate"));

    var payments = builder.AddDotnetProject(
        "payments",
        "../SubscriptionManagement.Payments/SubscriptionManagement.Payments.csproj"
      )
      .WithEnvironment("ConnectionStrings__Postgres", ConnectionString(paymentsDb, servicePoolSize))
      .WithReferenceRelationship(paymentsDb)
      .WithReference(messaging)
      .WaitFor(paymentsDb)
      .WaitFor(messaging)
      .WithHttpHealthCheck("/health/ready", endpointName: "http")
      .WithReplicas(replicas)
      .WithEnvironment("DOTNET_hostBuilder__reloadConfigOnChange", "false")
      .WithEnvironment(
        "Gateway__DeclineAtOrAbove",
        builder.Configuration.Required<string>("Gateway:DeclineAtOrAbove")
      )
      .WithEnvironment(
        "Gateway__NoReplyAtOrAbove",
        builder.Configuration.Required<string>("Gateway:NoReplyAtOrAbove")
      );

    var relay = builder.AddDotnetProject(
        "relay",
        "../SubscriptionManagement.Relay/SubscriptionManagement.Relay.csproj"
      )
      .WithEnvironment("ConnectionStrings__Postgres", ConnectionString(database, servicePoolSize))
      .WithReferenceRelationship(database)
      .WithReference(messaging)
      .WaitFor(database)
      .WaitFor(messaging)
      .WaitForCompletion(migrator)
      .WaitFor(api)
      .WithHttpHealthCheck("/health/ready", endpointName: "http")
      .WithReplicas(replicas)
      .WithEnvironment("DOTNET_hostBuilder__reloadConfigOnChange", "false")
      .WithEnvironment("Api__BaseUrl", api.GetEndpoint("http"))
      .WithEnvironment("Relay__PollIntervalMs", builder.Configuration.Required<string>("Relay:PollIntervalMs"))
      .WithEnvironment("Relay__BatchSize", builder.Configuration.Required<string>("Relay:BatchSize"))
      .WithParentRelationship(api);

    return new S2SServices(messaging, billing, payments, relay);
  }

  internal static ReferenceExpression ConnectionString(
    IResourceBuilder<PostgresDatabaseResource> database,
    int poolSize
  )
  {
    return ReferenceExpression.Create(
      $"{database.Resource.ConnectionStringExpression};Maximum Pool Size={poolSize.ToString()}"
    );
  }
}