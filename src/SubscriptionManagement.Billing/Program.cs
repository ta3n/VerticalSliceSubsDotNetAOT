using SubscriptionManagement.Billing;

// Two cleartext endpoints, declared in appsettings.json (Kestrel:Endpoints) because gRPC without TLS needs an
// HTTP/2-only port: "http" (HTTP/1.1, 5128) for probes and REST, "grpc" (HTTP/2, 5138) for BillingService. The launch
// profile has no applicationUrl on purpose; Aspire derives its "http" and "grpc" endpoints from that section.
var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);

// OpenTelemetry over OTLP when OTEL_EXPORTER_OTLP_ENDPOINT is set (see SubscriptionManagement.ServiceDefaults), plus
// Wolverine's handler spans and the RabbitMQ client's publish/deliver spans.
builder.AddServiceDefaults(
    ServiceSetup.ServiceName,
    ["Wolverine", "RabbitMQ.Client.Publisher", "RabbitMQ.Client.Subscriber"],
    ["Wolverine*"]
);

builder.AddBilling();

var app = builder.Build();

app.MapBilling();

await app.RunAsync();