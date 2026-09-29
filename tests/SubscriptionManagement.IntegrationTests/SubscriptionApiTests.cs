using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace SubscriptionManagement.IntegrationTests;

public sealed class SubscriptionApiTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Create_returns_201_with_location_and_the_original_contract_then_get_returns_the_same()
    {
        var response = await Client.PostAsJsonAsync("/subscriptions", NewSubscription(trial: "2026-09-12"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await ReadAsync(response);
        Assert.Equal($"/subscriptions/{Id(created)}", response.Headers.Location!.OriginalString);
        Assert.Equal("Netflix", created.GetProperty("serviceName").GetString());
        Assert.Equal(99000m, created.GetProperty("price").GetDecimal());
        Assert.Equal("2026-09-05", created.GetProperty("startDate").GetString());
        Assert.Equal("2026-09-12", created.GetProperty("trialEndsAt").GetString());
        Assert.Equal(1, created.GetProperty("status").GetInt32());
        Assert.Equal(6, created.EnumerateObject().Count()); // internal period columns are not exposed

        var fetched = await Client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal(created.ToString(), (await ReadAsync(fetched)).ToString());
    }

    [Fact]
    public async Task Create_accepts_missing_and_explicit_null_trial_and_a_100_char_name()
    {
        var noTrial = await CreateSubscriptionAsync(new { serviceName = new string('n', 100), price = 1, startDate = "2026-09-05" });
        Assert.Equal(System.Text.Json.JsonValueKind.Null, noTrial.GetProperty("trialEndsAt").ValueKind);

        var nullTrial = await CreateSubscriptionAsync(NewSubscription(trial: null));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, nullTrial.GetProperty("trialEndsAt").ValueKind);
    }

    [Fact]
    public async Task Create_preserves_the_smallest_positive_price_and_hostile_text_verbatim()
    {
        var body = new StringContent(
            """{"serviceName":"x'); DROP TABLE subscriptions;--","price":0.0000000000000000000000000001,"startDate":"2026-09-05"}""",
            Encoding.UTF8, "application/json");
        var response = await Client.PostAsync("/subscriptions", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = Id(await ReadAsync(response));

        var fetched = await ReadAsync(await Client.GetAsync($"/subscriptions/{id}"));
        Assert.Equal("x'); DROP TABLE subscriptions;--", fetched.GetProperty("serviceName").GetString());
        Assert.Equal(0.0000000000000000000000000001m, fetched.GetProperty("price").GetDecimal());
    }

    [Theory]
    [InlineData("""{"serviceName":"","price":1,"startDate":"2026-09-05"}""", "serviceName")]
    [InlineData("""{"price":1,"startDate":"2026-09-05"}""", "serviceName")]
    [InlineData("""{"serviceName":"   ","price":1,"startDate":"2026-09-05"}""", "serviceName")]
    [InlineData("""{"serviceName":"Netflix","price":0,"startDate":"2026-09-05"}""", "price")]
    [InlineData("""{"serviceName":"Netflix","price":-5,"startDate":"2026-09-05"}""", "price")]
    [InlineData("""{"serviceName":"Netflix","startDate":"2026-09-05"}""", "price")]
    [InlineData("""{"serviceName":"Netflix","price":1}""", "startDate")]
    [InlineData("""{"serviceName":"Netflix","price":1,"startDate":"2026-09-05","trialEndsAt":"2026-09-05"}""", "trialEndsAt")]
    [InlineData("""{"serviceName":"Netflix","price":1,"startDate":"2026-09-05","trialEndsAt":"2026-09-01"}""", "trialEndsAt")]
    public async Task Create_rejects_invalid_fields_with_a_problem_naming_the_field(string json, string field)
    {
        var response = await Client.PostAsync("/subscriptions", new StringContent(json, Encoding.UTF8, "application/json"));

        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation", "/subscriptions");
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out var messages));
        Assert.NotEmpty(messages.EnumerateArray());
    }

    [Fact]
    public async Task Create_rejects_a_name_over_100_chars()
    {
        var response = await Client.PostAsJsonAsync("/subscriptions", NewSubscription(name: new string('x', 101)));
        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
        Assert.True(problem.GetProperty("errors").TryGetProperty("serviceName", out _));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{"serviceName":"a","price":"abc","startDate":"2026-09-05"}""")]
    [InlineData("""{"serviceName":"a","price":1,"startDate":"05/09/2026"}""")]
    [InlineData("""{"serviceName":"a","price":1,"startDate":"2026-02-30"}""")]
    [InlineData("")]
    public async Task Create_rejects_malformed_bodies_with_400_problem(string json)
    {
        var response = await Client.PostAsync("/subscriptions", new StringContent(json, Encoding.UTF8, "application/json"));
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "malformed-request");
    }

    [Fact]
    public async Task Create_rejects_a_non_json_content_type_with_415_problem()
    {
        var response = await Client.PostAsync("/subscriptions", new StringContent("x", Encoding.UTF8, "text/plain"));
        await AssertProblemAsync(response, HttpStatusCode.UnsupportedMediaType, "unsupported-media-type");
    }

    [Fact]
    public async Task Get_unknown_subscription_is_a_404_problem_carrying_the_id()
    {
        var id = Guid.NewGuid();
        var problem = await AssertProblemAsync(await Client.GetAsync($"/subscriptions/{id}"),
            HttpStatusCode.NotFound, "subscription-not-found", $"/subscriptions/{id}");
        Assert.Equal(id.ToString(), problem.GetProperty("subscriptionId").GetString());
    }

    [Theory]
    [InlineData("GET", "/subscriptions/not-a-guid")]
    [InlineData("DELETE", "/subscriptions/not-a-guid")]
    [InlineData("POST", "/subscriptions/not-a-guid/pause")]
    public async Task Malformed_ids_are_400_problems(string method, string url)
    {
        var response = await Client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "malformed-request");
    }

    [Fact]
    public async Task Cancel_returns_204_keeps_the_row_and_a_second_cancel_is_400_with_the_legacy_message()
    {
        var id = Id(await CreateSubscriptionAsync());

        var first = await Client.DeleteAsync($"/subscriptions/{id}");
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var fetched = await ReadAsync(await Client.GetAsync($"/subscriptions/{id}"));
        Assert.Equal(2, fetched.GetProperty("status").GetInt32());

        var second = await AssertProblemAsync(await Client.DeleteAsync($"/subscriptions/{id}"),
            HttpStatusCode.BadRequest, "subscription-already-canceled");
        Assert.Equal("Subscription already canceled.", second.GetProperty("detail").GetString());
        Assert.Equal("Subscription already canceled.", second.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Cancel_unknown_subscription_is_404()
    {
        await AssertProblemAsync(await Client.DeleteAsync($"/subscriptions/{Guid.NewGuid()}"),
            HttpStatusCode.NotFound, "subscription-not-found");
    }

    [Fact]
    public async Task Overlapping_cancellations_serialise_exactly_one_succeeds()
    {
        var id = Id(await CreateSubscriptionAsync());

        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Client.DeleteAsync($"/subscriptions/{id}")));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.NoContent));
        Assert.Equal(11, responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest));
    }
}
