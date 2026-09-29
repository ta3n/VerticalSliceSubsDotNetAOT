using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Json;
using Npgsql;
using SubscriptionManagement.Migrator;
using StackExchange.Redis;

namespace SubscriptionManagement.IntegrationTests;

public sealed class CacheTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    private static string Key(string id) => $"subs:subscriptions:{id}";

    [Fact]
    public async Task Create_warms_the_cache_with_a_bounded_ttl_and_repeat_reads_skip_the_database()
    {
        var id = Id(await CreateSubscriptionAsync(NewSubscription("Original")));
        var db = Fixture.Redis.GetDatabase();

        Assert.True(await db.KeyExistsAsync(Key(id)));
        var ttl = await db.KeyTimeToLiveAsync(Key(id));
        Assert.InRange(ttl!.Value.TotalSeconds, 250, 300);

        // Change the row behind the API's back: a cache hit still returns the old value.
        await Fixture.ExecuteAsync("UPDATE subscriptions SET service_name = 'Changed' WHERE id = @id", ("id", Guid.Parse(id)));
        Assert.Equal("Original", (await ReadAsync(await Client.GetAsync($"/subscriptions/{id}"))).GetProperty("serviceName").GetString());

        // After the entry disappears the next read reloads from PostgreSQL and repopulates.
        await db.KeyDeleteAsync(Key(id));
        Assert.Equal("Changed", (await ReadAsync(await Client.GetAsync($"/subscriptions/{id}"))).GetProperty("serviceName").GetString());
        Assert.True(await db.KeyExistsAsync(Key(id)));
    }

    [Fact]
    public async Task Cancel_evicts_so_the_next_read_reloads_the_fresh_status()
    {
        var id = Id(await CreateSubscriptionAsync());
        var db = Fixture.Redis.GetDatabase();
        Assert.True(await db.KeyExistsAsync(Key(id)));

        await Client.DeleteAsync($"/subscriptions/{id}");
        Assert.False(await db.KeyExistsAsync(Key(id)));

        Assert.Equal(2, (await ReadAsync(await Client.GetAsync($"/subscriptions/{id}"))).GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Pause_resume_and_renew_refresh_the_cached_view()
    {
        var id = Id(await CreateSubscriptionAsync());

        await PostAsync($"/subscriptions/{id}/pause");
        Assert.Equal(3, await CachedStatusAsync(id));
        await PostAsync($"/subscriptions/{id}/resume");
        Assert.Equal(1, await CachedStatusAsync(id));
        await PostAsync($"/subscriptions/{id}/renew");
        Assert.Equal(1, await CachedStatusAsync(id));
    }

    [Fact]
    public async Task A_missing_subscription_is_not_cached_so_a_later_create_is_visible()
    {
        var missing = Guid.NewGuid().ToString();
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/subscriptions/{missing}")).StatusCode);
        Assert.False(await Fixture.Redis.GetDatabase().KeyExistsAsync(Key(missing)));
    }

    [Fact]
    public async Task Without_redis_the_api_falls_back_to_an_in_process_cache_and_still_works()
    {
        await using var factory = Fixture.CreateFactory(redis: false);
        using var client = factory.CreateClient();

        var created = await client.PostAsJsonAsync("/subscriptions", NewSubscription());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = Id(await ReadAsync(created));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/subscriptions/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/subscriptions/{id}")).StatusCode);
        Assert.Equal(2, (await ReadAsync(await client.GetAsync($"/subscriptions/{id}"))).GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task An_unreachable_redis_degrades_to_database_reads_instead_of_failing_requests()
    {
        await using var factory = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = Fixture.PostgresConnectionString,
                ["ConnectionStrings:Redis"] = "127.0.0.1:1,abortConnect=false,connectTimeout=200,syncTimeout=200,asyncTimeout=200",
            })));
        using var client = factory.CreateClient();

        var created = await client.PostAsJsonAsync("/subscriptions", NewSubscription());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = Id(await ReadAsync(created));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/subscriptions/{id}")).StatusCode);

        // Readiness depends on the database only; the aggregate health reports the cache as down.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health")).StatusCode);
    }

    private async Task<int> CachedStatusAsync(string id)
    {
        // RedisCache stores each entry as a hash; the payload lives in the "data" field.
        var value = await Fixture.Redis.GetDatabase().HashGetAsync(Key(id), "data");
        Assert.True(value.HasValue);
        using var json = System.Text.Json.JsonDocument.Parse(value.ToString());
        return json.RootElement.GetProperty("status").GetInt32();
    }
}

public sealed class PlatformTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task An_inbound_trace_id_is_echoed_and_appears_in_problems()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/subscriptions/{Guid.NewGuid()}");
        request.Headers.Add("X-Trace-Id", "caller-trace-42");

        var response = await Client.SendAsync(request);

        Assert.Equal("caller-trace-42", response.Headers.GetValues("X-Trace-Id").Single());
        var problem = await AssertProblemAsync(response, HttpStatusCode.NotFound, "subscription-not-found");
        Assert.Equal("caller-trace-42", problem.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task A_missing_trace_id_is_generated_per_request()
    {
        var a = (await Client.GetAsync("/plans")).Headers.GetValues("X-Trace-Id").Single();
        var b = (await Client.GetAsync("/plans")).Headers.GetValues("X-Trace-Id").Single();

        Assert.True(Guid.TryParse(a, out _));
        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("tab\there")]
    public async Task Unsafe_inbound_trace_ids_are_replaced(string value)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/plans");
        request.Headers.TryAddWithoutValidation("X-Trace-Id", value);
        var echoed = (await Client.SendAsync(request)).Headers.GetValues("X-Trace-Id").Single();
        Assert.NotEqual(value, echoed);
        Assert.True(Guid.TryParse(echoed, out _));
    }

    [Fact]
    public async Task An_oversized_inbound_trace_id_is_replaced()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/plans");
        request.Headers.Add("X-Trace-Id", new string('a', 500));
        var echoed = (await Client.SendAsync(request)).Headers.GetValues("X-Trace-Id").Single();
        Assert.True(Guid.TryParse(echoed, out _));
    }

    [Fact]
    public async Task Unknown_routes_and_wrong_methods_are_problems()
    {
        await AssertProblemAsync(await Client.GetAsync("/nope"), HttpStatusCode.NotFound, "not-found", "/nope");
        await AssertProblemAsync(await Client.PutAsync("/plans", null), HttpStatusCode.MethodNotAllowed, "method-not-allowed");
    }

    [Fact]
    public async Task Health_endpoints_report_up_and_do_not_leak_details()
    {
        foreach (var path in new[] { "/health/live", "/health/ready", "/health" })
        {
            var response = await Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await ReadAsync(response);
            Assert.Equal("UP", body.GetProperty("status").GetString());
            Assert.Single(body.EnumerateObject());
        }
    }

    [Fact]
    public async Task OpenApi_document_describes_the_endpoints_when_enabled()
    {
        var response = await Client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var paths = (await ReadAsync(response)).GetProperty("paths");
        Assert.True(paths.TryGetProperty("/subscriptions", out _));
        Assert.True(paths.TryGetProperty("/subscriptions/{id}/renew", out _));
        Assert.True(paths.TryGetProperty("/plans", out _));
        Assert.True(paths.TryGetProperty("/invoices/{id}/pay", out _));

        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/scalar")).StatusCode);
    }

    [Fact]
    public async Task OpenApi_is_not_exposed_unless_enabled()
    {
        // UseSetting (not ConfigureAppConfiguration): Program reads this flag while building, before later overrides apply.
        await using var factory = Fixture.CreateFactory(redis: false).WithWebHostBuilder(b => b.UseSetting("OpenApi:Enabled", "false"));
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/openapi/v1.json")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/scalar")).StatusCode);
    }

    [Fact]
    public async Task Migrations_are_idempotent_and_detect_tampering()
    {
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        Assert.Equal(0, await MigrationRunner.RunAsync(Fixture.PostgresConnectionString, logger));

        await Fixture.ExecuteAsync("UPDATE schema_migrations SET checksum = repeat('0', 64) WHERE version = 2");
        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => MigrationRunner.RunAsync(Fixture.PostgresConnectionString, logger));
            Assert.Contains("V2", ex.Message);
        }
        finally
        {
            var real = MigrationRunner.LoadEmbedded().Single(m => m.Version == 2).Checksum;
            await Fixture.ExecuteAsync("UPDATE schema_migrations SET checksum = @c WHERE version = 2", ("c", real));
        }

        Assert.Equal(0, await MigrationRunner.RunAsync(Fixture.PostgresConnectionString, logger));
    }

    [Fact]
    public async Task Concurrent_migrators_on_a_fresh_database_apply_each_migration_once()
    {
        var name = "fresh_" + Guid.NewGuid().ToString("N")[..8];
        await Fixture.ExecuteAsync($"CREATE DATABASE {name}");
        var builder = new NpgsqlConnectionStringBuilder(Fixture.PostgresConnectionString) { Database = name };
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        var applied = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => MigrationRunner.RunAsync(builder.ConnectionString, logger)));

        Assert.Equal(4, applied.Sum()); // V1..V4, each applied by exactly one runner
    }

    [Fact]
    public async Task The_schema_matches_the_reference_tables()
    {
        await using var connection = await Fixture.OpenDbAsync();
        await using var command = new NpgsqlCommand(
            "SELECT column_name FROM information_schema.columns WHERE table_name = 'subscriptions' ORDER BY ordinal_position", connection);
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        Assert.Equal(["id", "service_name", "price", "start_date", "trial_ends_at", "status", "current_period_start", "current_period_end"], columns);
    }
}
