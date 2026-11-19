using Grpc.Core;
using Grpc.Net.Client.Configuration;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Options;
using Npgsql;
using Scalar.AspNetCore;
using SubscriptionManagement.Contracts.Grpc;
using SubscriptionManagement.Features.Invoices.GetInvoice;
using SubscriptionManagement.Features.Invoices.GetInvoiceRenewal;
using SubscriptionManagement.Features.Invoices.ListInvoicesBySubscription;
using SubscriptionManagement.Features.Invoices.PayInvoice;
using SubscriptionManagement.Features.Invoices.SettleInvoice;
using SubscriptionManagement.Features.Invoices.VoidInvoice;
using SubscriptionManagement.Features.Plans.CreatePlan;
using SubscriptionManagement.Features.Plans.GetPlan;
using SubscriptionManagement.Features.Plans.ListPlans;
using SubscriptionManagement.Features.Subscriptions;
using SubscriptionManagement.Features.Subscriptions.CancelSubscriptions;
using SubscriptionManagement.Features.Subscriptions.CreateSubscriptions;
using SubscriptionManagement.Features.Subscriptions.GetSubscription;
using SubscriptionManagement.Features.Subscriptions.PauseSubscription;
using SubscriptionManagement.Features.Subscriptions.RenewSubscription;
using SubscriptionManagement.Features.Subscriptions.ResumeSubscription;
using SubscriptionManagement.Features.Subscriptions.SearchSubscriptions;
using SubscriptionManagement.Features.Subscriptions.StartRenewal;
using SubscriptionManagement.Infrastructure;
using SubscriptionManagement.Infrastructure.Billing;

// `SubscriptionManagement healthcheck [url]` probes the liveness endpoint and exits 0/1. It exists because the
// minimal runtime-deps image has no curl/wget for a container HEALTHCHECK.
if (args is ["healthcheck", ..]) return await HealthProbe.RunAsync(args);

var builder = WebApplication.CreateSlimBuilder(args);

builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);

builder.Services.ConfigureHttpJsonOptions(
    o =>
        o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default)
);

// Malformed ids / bodies must surface as exceptions so they render as problem+json (default is Development-only).
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);

builder.Services.AddOptions<CacheOptions>()
    .Bind(builder.Configuration.GetSection(CacheOptions.SectionName))
    .Validate(o => o.SubscriptionTtl > TimeSpan.Zero, "Cache:SubscriptionTtl must be positive.");

builder.Services.AddSingleton(TimeProvider.System);

// Resolved lazily from the final IConfiguration, and fails fast (see the eager resolve below).
builder.Services.AddSingleton(
    sp =>
    {
        var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");

        return NpgsqlDataSource.Create(connectionString);
    }
);

// Redis when ConnectionStrings:Redis is set, otherwise a process-local cache (same as the Java "simple" cache type).
builder.Services.AddSingleton<IDistributedCache>(
    sp =>
    {
        var redis = sp.GetRequiredService<IConfiguration>().GetConnectionString("Redis");
        return string.IsNullOrWhiteSpace(redis)
            ? new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()))
            : new RedisCache(Options.Create(new RedisCacheOptions { Configuration = redis, InstanceName = "subs:" }));
    }
);
builder.Services.AddSingleton<SubscriptionCache>();

// Messages for the other services (transactional outbox); off unless Outbox:Enabled=true, which needs a Relay
// draining the table. Set by the Aspire AppHost.
builder.Services.AddSingleton(
    sp => new OutboxOptions(sp.GetRequiredService<IConfiguration>().GetValue<bool>("Outbox:Enabled"))
);

// Billing over gRPC (renewal quotes, saga status); off unless Billing:GrpcAddress is set, again by the AppHost.
// A plain address instead of service discovery keeps that package out of the native binary.
var billingAddress = builder.Configuration["Billing:GrpcAddress"];
if (string.IsNullOrWhiteSpace(billingAddress))
{
    builder.Services.AddSingleton<IBillingGateway, UnconfiguredBillingGateway>();
}
else
{
    var billingUri = new Uri(billingAddress); // parsed here so a malformed address fails at startup
    builder.Services.AddGrpcClient<BillingService.BillingServiceClient>(o => o.Address = billingUri)
        .ConfigureChannel(
            o => o.ServiceConfig = new ServiceConfig
            {
                MethodConfigs =
                {
                    new MethodConfig
                    {
                        Names = { MethodName.Default },
                        // Both calls are read-only, so retrying a call that never reached Billing is safe.
                        RetryPolicy = new RetryPolicy
                        {
                            MaxAttempts = 3,
                            InitialBackoff = TimeSpan.FromMilliseconds(100),
                            MaxBackoff = TimeSpan.FromSeconds(1),
                            BackoffMultiplier = 2,
                            RetryableStatusCodes = { StatusCode.Unavailable }
                        }
                    }
                }
            }
        );
    // Transient like the generated client it wraps, so the client factory keeps managing the connection.
    builder.Services.AddTransient<IBillingGateway, GrpcBillingGateway>();
}

// OpenTelemetry (logs, traces, metrics) over OTLP when OTEL_EXPORTER_OTLP_ENDPOINT is set — by the Aspire AppHost,
// compose or Helm. See SubscriptionManagement.ServiceDefaults.
builder.AddServiceDefaults("subscription-management");

// Access-token (Keycloak) authentication; off unless Auth:Authority is set, by the Aspire AppHost.
builder.AddAccessTokenAuth();

// API description + Scalar UI; off unless OpenApi:Enabled=true (enabled in appsettings.Development.json).
var openApiEnabled = builder.Configuration.GetValue<bool>("OpenApi:Enabled");
if (openApiEnabled) builder.Services.AddOpenApi();

builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
    .AddCheck<CacheHealthCheck>("cache");

var app = builder.Build();

// Fail fast on a missing/invalid database configuration instead of on the first request.
_ = app.Services.GetRequiredService<NpgsqlDataSource>();

app.UseMiddleware<CorrelationMiddleware>();
app.UseExceptionHandler(_ => { }); // the registered IExceptionHandler writes the response
app.UseStatusCodePages(
    context => ProblemWriter.WriteAsync(
        context.HttpContext,
        context.HttpContext.Response.StatusCode,
        context.HttpContext.Response.StatusCode switch
        {
            404 => "not-found",
            405 => "method-not-allowed",
            415 => "unsupported-media-type",
            _ => "http-error"
        },
        context.HttpContext.Response.StatusCode switch
        {
            404 => "Resource not found",
            405 => "Method not allowed",
            415 => "Unsupported media type",
            _ => "The request failed"
        },
        null
    )
);

app.UseAccessTokenAuth();

CreateSubscriptionEndpoint.Map(app);
GetSubscriptionEndpoint.Map(app);
CancelSubscriptionEndpoint.Map(app);
PauseSubscriptionEndpoint.Map(app);
ResumeSubscriptionEndpoint.Map(app);
RenewSubscriptionEndpoint.Map(app);
StartRenewalEndpoint.Map(app);
SearchSubscriptionsEndpoint.Map(app);
ListInvoicesBySubscriptionEndpoint.Map(app);

CreatePlanEndpoint.Map(app);
GetPlanEndpoint.Map(app);
ListPlansEndpoint.Map(app);

GetInvoiceEndpoint.Map(app);
PayInvoiceEndpoint.Map(app);
GetInvoiceRenewalEndpoint.Map(app);
SettleInvoiceEndpoint.Map(app);
VoidInvoiceEndpoint.Map(app);

app.MapHealth();

if (openApiEnabled)
{
    app.MapOpenApi().AllowAnonymous(); // /openapi/v1.json
    app.MapScalarApiReference().AllowAnonymous(); // /scalar
}

await app.RunAsync();
return 0;