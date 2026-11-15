namespace AppHost;

internal sealed record S2SServices(
    IResourceBuilder<RabbitMQServerResource> Messaging,
    IResourceBuilder<ProjectResource> Billing,
    IResourceBuilder<ProjectResource> Payments,
    IResourceBuilder<ProjectResource> Relay
);

internal static class Services
{
    public static S2SServices AddS2SServices(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<PostgresServerResource> postgres,
        IResourceBuilder<PostgresDatabaseResource> database,
        IResourceBuilder<ProjectResource> migrator,
        IResourceBuilder<ProjectResource> api,
        int servicePoolSize
    )
    {
        var messaging = builder.AddRabbitMQ("messaging")
            .WithImageTag("4.3-management")
            .WithManagementPlugin()
            .WithDataVolume();
        var billingDb = postgres.AddDatabase("billing-db", "billing");
        var paymentsDb = postgres.AddDatabase("payments-db", "payments");

        var billing = builder.AddProject<Projects.SubscriptionManagement_Billing>("billing")
            .WithEnvironment("ConnectionStrings__Postgres", ConnectionString(billingDb, servicePoolSize))
            .WithReferenceRelationship(billingDb)
            .WithReference(messaging)
            .WaitFor(billingDb)
            .WaitFor(messaging)
            .WithHttpHealthCheck("/health/ready", endpointName: "http")
            .WithEnvironment("DOTNET_hostBuilder__reloadConfigOnChange", "false")
            .WithEnvironment(
                "Billing__RenewalTimeoutSeconds",
                builder.Configuration.Required<string>("Billing:RenewalTimeoutSeconds")
            )
            .WithEnvironment("Billing__TaxRate", builder.Configuration.Required<string>("Billing:TaxRate"));

        var payments = builder.AddProject<Projects.SubscriptionManagement_Payments>("payments")
            .WithEnvironment("ConnectionStrings__Postgres", ConnectionString(paymentsDb, servicePoolSize))
            .WithReferenceRelationship(paymentsDb)
            .WithReference(messaging)
            .WaitFor(paymentsDb)
            .WaitFor(messaging)
            .WithHttpHealthCheck("/health/ready", endpointName: "http")
            .WithEnvironment("DOTNET_hostBuilder__reloadConfigOnChange", "false")
            .WithEnvironment(
                "Gateway__DeclineAtOrAbove",
                builder.Configuration.Required<string>("Gateway:DeclineAtOrAbove")
            )
            .WithEnvironment(
                "Gateway__NoReplyAtOrAbove",
                builder.Configuration.Required<string>("Gateway:NoReplyAtOrAbove")
            );

        var relay = builder.AddProject<Projects.SubscriptionManagement_Relay>("relay")
            .WithEnvironment("ConnectionStrings__Postgres", ConnectionString(database, servicePoolSize))
            .WithReferenceRelationship(database)
            .WithReference(messaging)
            .WaitFor(database)
            .WaitFor(messaging)
            .WaitForCompletion(migrator)
            .WaitFor(api)
            .WithHttpHealthCheck("/health/ready", endpointName: "http")
            .WithEnvironment("DOTNET_hostBuilder__reloadConfigOnChange", "false")
            .WithEnvironment("Api__BaseUrl", api.GetEndpoint("http"))
            .WithEnvironment("Relay__PollIntervalMs", builder.Configuration.Required<string>("Relay:PollIntervalMs"))
            .WithEnvironment("Relay__BatchSize", builder.Configuration.Required<string>("Relay:BatchSize"))
            .WithParentRelationship(api);

        return new S2SServices(messaging, billing, payments, relay);
    }

    private static ReferenceExpression ConnectionString(
        IResourceBuilder<PostgresDatabaseResource> database,
        int poolSize
    )
    {
        return ReferenceExpression.Create(
            $"{database.Resource.ConnectionStringExpression};Maximum Pool Size={poolSize.ToString()}"
        );
    }
}