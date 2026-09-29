using System.Diagnostics;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Options;
using Npgsql;
using SubscriptionManagement.Features.Invoices.GetInvoice;
using SubscriptionManagement.Features.Invoices.ListInvoicesBySubscription;
using SubscriptionManagement.Features.Invoices.PayInvoice;
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
using SubscriptionManagement.Infrastructure;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default));

// Malformed ids / bodies must surface as exceptions so they render as problem+json (default is Development-only).
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);

builder.Services.AddOptions<CacheOptions>()
    .Bind(builder.Configuration.GetSection(CacheOptions.SectionName))
    .Validate(o => o.SubscriptionTtl > TimeSpan.Zero, "Cache:SubscriptionTtl must be positive.");

builder.Services.AddSingleton(TimeProvider.System);

// Resolved lazily from the final IConfiguration, and fails fast (see the eager resolve below).
builder.Services.AddSingleton(sp =>
{
    var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("Postgres");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");
    }

    return NpgsqlDataSource.Create(connectionString);
});

// Redis when ConnectionStrings:Redis is set, otherwise a process-local cache (same as the Java "simple" cache type).
builder.Services.AddSingleton<IDistributedCache>(sp =>
{
    var redis = sp.GetRequiredService<IConfiguration>().GetConnectionString("Redis");
    return string.IsNullOrWhiteSpace(redis)
        ? new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()))
        : new RedisCache(Options.Create(new RedisCacheOptions { Configuration = redis, InstanceName = "subs:" }));
});
builder.Services.AddSingleton<SubscriptionCache>();

builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
    .AddCheck<CacheHealthCheck>("cache");

var app = builder.Build();

// Fail fast on a missing/invalid database configuration instead of on the first request.
_ = app.Services.GetRequiredService<NpgsqlDataSource>();

app.UseMiddleware<CorrelationMiddleware>();
app.UseExceptionHandler(_ => { }); // the registered IExceptionHandler writes the response
app.UseStatusCodePages(context => ProblemWriter.WriteAsync(
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
    null));

CreateSubscriptionEndpoint.Map(app);
GetSubscriptionEndpoint.Map(app);
CancelSubscriptionEndpoint.Map(app);
PauseSubscriptionEndpoint.Map(app);
ResumeSubscriptionEndpoint.Map(app);
RenewSubscriptionEndpoint.Map(app);
SearchSubscriptionsEndpoint.Map(app);
ListInvoicesBySubscriptionEndpoint.Map(app);

CreatePlanEndpoint.Map(app);
GetPlanEndpoint.Map(app);
ListPlansEndpoint.Map(app);

GetInvoiceEndpoint.Map(app);
PayInvoiceEndpoint.Map(app);

app.MapHealth();

app.Run();

/// <summary>Exposed for <c>WebApplicationFactory&lt;Program&gt;</c> in the integration tests.</summary>
public partial class Program;
