using SubscriptionManagement.Relay;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);

// OpenTelemetry over OTLP when OTEL_EXPORTER_OTLP_ENDPOINT is set (see SubscriptionManagement.ServiceDefaults), plus
// Wolverine's handler spans, the RabbitMQ client's publish/deliver spans and the outbox drain's own spans.
builder.AddServiceDefaults(
    ServiceSetup.ServiceName,
    ["Wolverine", "RabbitMQ.Client.Publisher", "RabbitMQ.Client.Subscriber", ServiceSetup.ActivitySourceName],
    ["Wolverine*"]
);

builder.AddRelay();

var app = builder.Build();

app.MapRelay();

await app.RunAsync();