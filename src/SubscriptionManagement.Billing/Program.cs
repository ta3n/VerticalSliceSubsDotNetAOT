using SubscriptionManagement.Billing;
using SubscriptionManagement.Secrets;

// Two cleartext endpoints, declared in appsettings.json (Kestrel:Endpoints) because gRPC without TLS needs an
// HTTP/2-only port: "http" (HTTP/1.1, 5128) for probes and REST, "grpc" (HTTP/2, 5138) for BillingService. The launch
// profile has no applicationUrl on purpose; Aspire derives its "http" and "grpc" endpoints from that section.
var builder = WebApplication.CreateBuilder(args);

// Secrets of the Bitwarden project become configuration (only when the AppHost enabled Bitwarden).
builder.AddBitwardenSecrets();

builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);

// OpenTelemetry over OTLP when OTEL_EXPORTER_OTLP_ENDPOINT is set (see SubscriptionManagement.ServiceDefaults), plus
// Wolverine's handler spans and the RabbitMQ client's publish/deliver spans.
builder.AddServiceDefaults(
  ServiceSetup.ServiceName,
  ["Wolverine", "RabbitMQ.Client.Publisher", "RabbitMQ.Client.Subscriber"],
  ["Wolverine*"]
);

// Access-token (Keycloak) authentication; off unless Auth:Authority is set, by the Aspire AppHost.
builder.AddAccessTokenAuth();

builder.AddBilling();

var app = builder.Build();

app.UseAccessTokenAuth();

app.MapBilling();

await app.RunAsync();