using System.Net;
using Npgsql;

namespace SubscriptionManagement.Relay.Tests;

/// <summary>The service boots from <see cref="ServiceSetup"/> against an empty database and answers its probes.</summary>
[Collection(RelayCollection.Name)]
public class HostSmokeTests(
    RelayFixture fixture
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

    /// <summary>The database belongs to the API: the migrator owns <c>public</c>, the Relay only its own schema.</summary>
    [Fact]
    public async Task Startup_creates_the_inbox_and_outbox_tables_in_the_relay_schema_only()
    {
        await using var connection = await fixture.OpenDbAsync();
        await using var command = new NpgsqlCommand(
            "SELECT table_schema, table_name FROM information_schema.tables "
            + "WHERE table_schema NOT IN ('pg_catalog', 'information_schema')",
            connection
        );
        var tables = new List<(string Schema, string Name)>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) tables.Add((reader.GetString(0), reader.GetString(1)));
        }

        Assert.Contains(("relay", "wolverine_incoming_envelopes"), tables);
        Assert.Contains(("relay", "wolverine_outgoing_envelopes"), tables);
        Assert.Contains(("relay", "wolverine_dead_letters"), tables);
        Assert.All(tables.Where(t => t.Name.StartsWith("wolverine_")), t => Assert.Equal("relay", t.Schema));
    }
}