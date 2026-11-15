using System.Net;
using Npgsql;

namespace SubscriptionManagement.Payments.Tests;

/// <summary>The service boots from <see cref="ServiceSetup"/> against an empty database and answers its probes.</summary>
[Collection(PaymentsCollection.Name)]
public class HostSmokeTests(
    PaymentsFixture fixture
)
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Probes_answer_200_once_the_host_is_up(
        string path
    )
    {
        var response = await fixture.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Startup_creates_the_inbox_and_outbox_tables_in_the_payments_schema()
    {
        await using var connection = await fixture.OpenDbAsync();
        await using var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'payments'",
            connection
        );
        var tables = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        }

        Assert.Contains("wolverine_incoming_envelopes", tables);
        Assert.Contains("wolverine_outgoing_envelopes", tables);
        Assert.Contains("wolverine_dead_letters", tables);
    }
}