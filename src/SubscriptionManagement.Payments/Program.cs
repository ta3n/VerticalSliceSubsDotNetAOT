using SubscriptionManagement.Payments;
using SubscriptionManagement.Secrets;

var builder = WebApplication.CreateBuilder(args);

// Secrets of the Bitwarden project become configuration (only when the AppHost enabled Bitwarden).
builder.AddBitwardenSecrets();

builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);

// OpenTelemetry over OTLP when OTEL_EXPORTER_OTLP_ENDPOINT is set (see SubscriptionManagement.ServiceDefaults), plus
// Wolverine's handler spans, the RabbitMQ client's publish/deliver spans and Marten's session spans.
builder.AddServiceDefaults(
  ServiceSetup.ServiceName,
  ["Wolverine", "RabbitMQ.Client.Publisher", "RabbitMQ.Client.Subscriber", "Marten"],
  ["Wolverine*", "Marten"]
);

// Access-token (Keycloak) authentication; off unless Auth:Authority is set, by the Aspire AppHost.
builder.AddAccessTokenAuth();

builder.AddPayments();

var app = builder.Build();

app.UseAccessTokenAuth();

app.MapPayments();

await app.RunAsync();