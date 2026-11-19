using SubscriptionManagement.Reporting;
using SubscriptionManagement.Secrets;

var builder = WebApplication.CreateBuilder(args);

// Secrets of the Bitwarden project become configuration (only when the AppHost enabled Bitwarden).
builder.AddBitwardenSecrets();

builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);

// OpenTelemetry over OTLP when OTEL_EXPORTER_OTLP_ENDPOINT is set (see SubscriptionManagement.ServiceDefaults), plus
// Wolverine's handler spans.
builder.AddServiceDefaults(ServiceSetup.ServiceName, ["Wolverine"], ["Wolverine*"]);

// Access-token (Keycloak) authentication; off unless Auth:Authority is set, by the Aspire AppHost.
builder.AddAccessTokenAuth();

builder.AddReporting();

var app = builder.Build();

app.UseAccessTokenAuth();

app.MapReporting();

await app.RunAsync();