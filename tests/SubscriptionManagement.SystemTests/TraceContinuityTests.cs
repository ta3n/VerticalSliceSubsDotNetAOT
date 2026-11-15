using System.Collections.Concurrent;
using System.Diagnostics;

namespace SubscriptionManagement.SystemTests;

/// <summary>
/// "One renewal is one connected trace": the claim the Grafana Request flow dashboard rests on. Every service runs in
/// this process, so one <see cref="ActivityListener"/> sees the spans that each service would export, and the trace
/// id of the inbound request must appear on every hop: HTTP, gRPC, outbox relay, broker publish and consume,
/// message handlers.
/// </summary>
public sealed class TraceContinuityTests(
    SystemFixture system
) : SystemTestBase(system)
{
    [Fact]
    public async Task One_renewal_request_is_one_trace_across_http_grpc_the_outbox_the_broker_and_every_handler()
    {
        var spans = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add
        };
        ActivitySource.AddActivityListener(listener);
        var traceId = ActivityTraceId.CreateRandom();
        var traceparent = $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01";

        var renewal = await StartRenewalAsync(SystemFixture.CapturedPrice, traceparent);
        await SettledAsync(renewal);
        await InvoiceWithStatusAsync(renewal, "PAID");

        var inTrace = spans.Where(s => s.TraceId == traceId).ToList();
        var sources = inTrace.Select(s => s.Source.Name).ToHashSet();
        foreach (var source in new[]
                 {
                     "Microsoft.AspNetCore", "Grpc.Net.Client", "SubscriptionManagement.Relay", "Wolverine",
                     "RabbitMQ.Client.Publisher", "RabbitMQ.Client.Subscriber"
                 })
            Assert.True(sources.Contains(source), $"no '{source}' span in the trace; seen: {string.Join(", ", sources)}");

        // The API serves the renewal request, then the Relay's settle call: two server spans in one trace.
        Assert.True(
            inTrace.Count(s => s.Source.Name == "Microsoft.AspNetCore" && s.Kind == ActivityKind.Server) >= 2,
            "the Relay's call back into the API is not part of the renewal's trace"
        );
        // Billing, Payments and the Relay each consume at least one message of the saga.
        var consumers = inTrace.Where(s => s.Source.Name == "RabbitMQ.Client.Subscriber").ToList();
        Assert.True(consumers.Count >= 3, $"expected consume spans from the broker hops, got {consumers.Count}");
    }
}
