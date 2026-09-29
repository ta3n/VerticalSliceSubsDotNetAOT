using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SubscriptionManagement.IntegrationTests;

public sealed class SearchApiTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    private async Task ResetAsync()
    {
        await Fixture.ExecuteAsync("TRUNCATE invoices, subscriptions");
    }

    private Task<HttpResponseMessage> SearchAsync(object? body) => Client.PostAsJsonAsync("/subscriptions/search", body);

    private static string[] Ids(JsonElement page) => page.GetProperty("items").EnumerateArray().Select(Id).ToArray();

    [Fact]
    public async Task An_empty_or_missing_body_returns_the_first_unfiltered_page_newest_start_date_first()
    {
        await ResetAsync();
        var oldest = Id(await CreateSubscriptionAsync(NewSubscription("A", start: "2026-01-01")));
        var newest = Id(await CreateSubscriptionAsync(NewSubscription("B", start: "2026-03-01")));
        var middle = Id(await CreateSubscriptionAsync(NewSubscription("C", start: "2026-02-01")));

        var noBody = await Client.PostAsync("/subscriptions/search", content: null);
        Assert.Equal(HttpStatusCode.OK, noBody.StatusCode);
        Assert.Equal([newest, middle, oldest], Ids(await ReadAsync(noBody)));

        var emptyObject = await ReadAsync(await SearchAsync(new { }));
        Assert.Equal([newest, middle, oldest], Ids(emptyObject));
        Assert.Equal(JsonValueKind.Null, emptyObject.GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task Filters_by_status()
    {
        await ResetAsync();
        var active = Id(await CreateSubscriptionAsync());
        var canceled = Id(await CreateSubscriptionAsync());
        var paused = Id(await CreateSubscriptionAsync());
        await Client.DeleteAsync($"/subscriptions/{canceled}");
        await PostAsync($"/subscriptions/{paused}/pause");

        Assert.Equal([active], Ids(await ReadAsync(await SearchAsync(new { status = "ACTIVE" }))));
        Assert.Equal([canceled], Ids(await ReadAsync(await SearchAsync(new { status = "CANCELED" }))));
        Assert.Equal([paused], Ids(await ReadAsync(await SearchAsync(new { status = "PAUSED" }))));
    }

    [Fact]
    public async Task Pages_through_all_rows_with_the_cursor_without_gaps_or_duplicates_including_start_date_ties()
    {
        await ResetAsync();
        var created = new List<string>();
        for (var i = 0; i < 7; i++)
        {
            // Pairs share a start date so the id tiebreaker is exercised.
            created.Add(Id(await CreateSubscriptionAsync(NewSubscription($"S{i}", start: $"2026-01-{10 + i / 2:00}"))));
        }

        var seen = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await ReadAsync(await SearchAsync(new { limit = 3, cursor }));
            seen.AddRange(Ids(page));
            cursor = page.GetProperty("nextCursor").GetString();
            pages++;
        }
        while (cursor is not null && pages < 10);

        Assert.Equal(3, pages);
        Assert.Equal(7, seen.Count);
        Assert.Equal(created.OrderBy(x => x).ToArray(), seen.OrderBy(x => x).ToArray());
        Assert.Equal(seen.Count, seen.Distinct().Count());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(101)]
    [InlineData(100000)]
    public async Task Out_of_range_limits_fall_back_to_the_default_of_20(int limit)
    {
        await ResetAsync();
        for (var i = 0; i < 22; i++)
        {
            await CreateSubscriptionAsync(NewSubscription($"S{i}", start: "2026-05-05"));
        }

        var page = await ReadAsync(await SearchAsync(new { limit }));
        Assert.Equal(20, Ids(page).Length);
        Assert.NotEqual(JsonValueKind.Null, page.GetProperty("nextCursor").ValueKind);
    }

    [Theory]
    [InlineData("active")]
    [InlineData("DELETED")]
    [InlineData("ACTIVE' OR '1'='1")]
    public async Task An_unknown_status_is_a_400_problem(string status)
    {
        var problem = await AssertProblemAsync(await SearchAsync(new { status }),
            HttpStatusCode.BadRequest, "invalid-subscription-status");
        Assert.Contains(status, problem.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("garbage!!")]
    [InlineData("bm9waXBl")]
    public async Task A_malformed_cursor_is_a_400_problem(string cursor)
    {
        await AssertProblemAsync(await SearchAsync(new { cursor }), HttpStatusCode.BadRequest, "invalid-cursor");
    }
}
