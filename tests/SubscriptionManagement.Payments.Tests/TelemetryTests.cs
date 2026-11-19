using System.Diagnostics;
using System.Diagnostics.Metrics;
using SubscriptionManagement.Contracts;

namespace SubscriptionManagement.Payments.Tests;

/// <summary>Program.cs exports the "Wolverine" and "Marten" sources and the "Marten" meter; a handled message must feed them.</summary>
[Collection(PaymentsCollection.Name)]
public class TelemetryTests(
    PaymentsFixture fixture
)
{
    [Fact]
    public async Task Handling_a_charge_emits_wolverine_and_marten_spans_and_counts_the_appended_events()
    {
        var spans = new List<(string Source, string Name)>();
        using var activities = new ActivityListener
        {
            ShouldListenTo = source => source.Name is "Wolverine" or "Marten",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                lock (spans)
                {
                    spans.Add((activity.Source.Name, activity.OperationName));
                }
            }
        };
        ActivitySource.AddActivityListener(activities);

        var appended = new List<string>();
        using var meters = new MeterListener();
        meters.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "Marten")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meters.SetMeasurementEventCallback<long>(
            (instrument, _, tags, _) =>
            {
                lock (appended)
                {
                    appended.AddRange(tags.ToArray().Select(t => $"{instrument.Name}:{t.Key}={t.Value}"));
                }
            }
        );
        meters.Start();

        await fixture.DeliverAsync(
            new ChargePayment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 11m, "USD", new DateOnly(2027, 1, 15))
        );

        meters.Dispose();
        activities.Dispose();

        // The handler span, the session it ran in and the reply leaving through the outbox.
        Assert.Contains(("Wolverine", "SubscriptionManagement.Contracts.ChargePayment"), spans);
        Assert.Contains(("Marten", "marten.connection"), spans);
        Assert.Contains(("Wolverine", "send"), spans);
        Assert.Contains("marten.event.append:event.type=charge_requested", appended);
        Assert.Contains("marten.event.append:event.type=charge_captured", appended);
    }
}