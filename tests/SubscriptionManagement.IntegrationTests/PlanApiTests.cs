using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace SubscriptionManagement.IntegrationTests;

public sealed class PlanApiTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    private static string NewCode() => "plan-" + Guid.NewGuid().ToString("N")[..12];

    private static object NewPlan(string code, string interval = "MONTH", decimal amount = 12m, int trialDays = 14) =>
        new { code, name = "Test plan", amount, currency = "USD", interval, trialDays };

    [Fact]
    public async Task The_seeded_catalog_is_listed_ordered_by_code()
    {
        var list = await ReadAsync(await Client.GetAsync("/plans"));
        var codes = list.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("code").GetString()!).ToArray();

        Assert.Contains("free", codes);
        Assert.Contains("pro-monthly", codes);
        Assert.Contains("pro-yearly", codes);
        Assert.Equal(codes.OrderBy(c => c, StringComparer.Ordinal).ToArray(), codes);

        var proYearly = list.GetProperty("items").EnumerateArray().First(p => p.GetProperty("code").GetString() == "pro-yearly");
        Assert.Equal(120m, proYearly.GetProperty("amount").GetDecimal());
        Assert.Equal("YEAR", proYearly.GetProperty("interval").GetString());
        Assert.Equal(14, proYearly.GetProperty("trialDays").GetInt32());
        Assert.True(proYearly.GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task Create_returns_201_with_location_and_the_plan_can_be_read_back()
    {
        var code = NewCode();
        var response = await Client.PostAsJsonAsync("/plans", NewPlan(code, "YEAR", 99.5m, 30));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/plans/{code}", response.Headers.Location!.OriginalString);

        var fetched = await ReadAsync(await Client.GetAsync($"/plans/{code}"));
        Assert.Equal(code, fetched.GetProperty("code").GetString());
        Assert.Equal(99.5m, fetched.GetProperty("amount").GetDecimal());
        Assert.Equal("YEAR", fetched.GetProperty("interval").GetString());
        Assert.Equal(30, fetched.GetProperty("trialDays").GetInt32());
    }

    [Fact]
    public async Task Creating_a_duplicate_code_is_a_409_and_concurrent_duplicates_yield_exactly_one_201()
    {
        var code = NewCode();
        Assert.Equal(HttpStatusCode.Created, (await Client.PostAsJsonAsync("/plans", NewPlan(code))).StatusCode);

        var problem = await AssertProblemAsync(await Client.PostAsJsonAsync("/plans", NewPlan(code)),
            HttpStatusCode.Conflict, "plan-already-exists");
        Assert.Equal(code, problem.GetProperty("planCode").GetString());

        var raced = NewCode();
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Client.PostAsJsonAsync("/plans", NewPlan(raced))));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Get_unknown_or_inactive_plan_is_404()
    {
        await AssertProblemAsync(await Client.GetAsync("/plans/does-not-exist"), HttpStatusCode.NotFound, "plan-not-found");

        var code = NewCode();
        await Client.PostAsJsonAsync("/plans", NewPlan(code));
        await Fixture.ExecuteAsync("UPDATE plans SET active = false WHERE code = @code", ("code", code));

        await AssertProblemAsync(await Client.GetAsync($"/plans/{code}"), HttpStatusCode.NotFound, "plan-not-found");
        var list = await ReadAsync(await Client.GetAsync("/plans"));
        Assert.DoesNotContain(list.GetProperty("items").EnumerateArray(), p => p.GetProperty("code").GetString() == code);
    }

    [Theory]
    [InlineData("""{"code":"","name":"n","amount":1,"currency":"USD","interval":"MONTH","trialDays":0}""", "code")]
    [InlineData("""{"code":"c","name":"","amount":1,"currency":"USD","interval":"MONTH","trialDays":0}""", "name")]
    [InlineData("""{"code":"c","name":"n","currency":"USD","interval":"MONTH","trialDays":0}""", "amount")]
    [InlineData("""{"code":"c","name":"n","amount":-1,"currency":"USD","interval":"MONTH","trialDays":0}""", "amount")]
    [InlineData("""{"code":"c","name":"n","amount":1,"currency":"US","interval":"MONTH","trialDays":0}""", "currency")]
    [InlineData("""{"code":"c","name":"n","amount":1,"currency":"USD","interval":"WEEK","trialDays":0}""", "interval")]
    [InlineData("""{"code":"c","name":"n","amount":1,"currency":"USD","interval":"month","trialDays":0}""", "interval")]
    [InlineData("""{"code":"c","name":"n","amount":1,"currency":"USD","interval":"MONTH","trialDays":-1}""", "trialDays")]
    [InlineData("""{"code":"c","name":"n","amount":1,"currency":"USD","interval":"MONTH","trialDays":366}""", "trialDays")]
    public async Task Create_rejects_invalid_fields_by_name(string json, string field)
    {
        var response = await Client.PostAsync("/plans", new StringContent(json, Encoding.UTF8, "application/json"));
        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task Create_rejects_overlong_code_and_name()
    {
        var response = await Client.PostAsJsonAsync("/plans", new { code = new string('c', 65), name = new string('n', 201), amount = 1, currency = "USD", interval = "MONTH" });
        var errors = (await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation")).GetProperty("errors");
        Assert.True(errors.TryGetProperty("code", out _));
        Assert.True(errors.TryGetProperty("name", out _));
    }
}
