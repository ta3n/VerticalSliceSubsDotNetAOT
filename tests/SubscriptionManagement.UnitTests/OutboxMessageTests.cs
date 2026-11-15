using System.Text.Json;
using SubscriptionManagement.Contracts;
using SubscriptionManagement.Infrastructure;

namespace SubscriptionManagement.UnitTests;

/// <summary>
/// The outbox payload is a contract with the Relay, which reads it with the reflection-based web defaults: the
/// source-generated JSON written by the API must stay camelCase with ISO dates and round-trip to the same record.
/// </summary>
public class OutboxMessageTests
{
    private static readonly JsonSerializerOptions RelayOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void InvoiceIssued_is_written_as_camel_case_json_the_relay_reads_back()
    {
        var message = new InvoiceIssued(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            13.75m,
            "USD",
            new DateOnly(2026, 9, 12),
            new DateOnly(2026, 10, 12)
        );

        var json = JsonSerializer.Serialize(message, AppJsonSerializerContext.Default.InvoiceIssued);

        Assert.Equal(
            """{"invoiceId":"11111111-1111-1111-1111-111111111111","subscriptionId":"22222222-2222-2222-2222-222222222222","amount":13.75,"currency":"USD","periodStart":"2026-09-12","periodEnd":"2026-10-12"}""",
            json
        );
        Assert.Equal(message, JsonSerializer.Deserialize<InvoiceIssued>(json, RelayOptions));
    }

    [Fact]
    public void SubscriptionCanceled_is_written_as_camel_case_json_the_relay_reads_back()
    {
        var message = new SubscriptionCanceled(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            new DateOnly(2026, 9, 30)
        );

        var json = JsonSerializer.Serialize(message, AppJsonSerializerContext.Default.SubscriptionCanceled);

        Assert.Equal("""{"subscriptionId":"22222222-2222-2222-2222-222222222222","canceledOn":"2026-09-30"}""", json);
        Assert.Equal(message, JsonSerializer.Deserialize<SubscriptionCanceled>(json, RelayOptions));
    }

    [Fact]
    public void The_outbox_type_names_are_the_record_names()
    {
        Assert.Equal("InvoiceIssued", OutboxMessageTypes.InvoiceIssued);
        Assert.Equal("SubscriptionCanceled", OutboxMessageTypes.SubscriptionCanceled);
    }
}