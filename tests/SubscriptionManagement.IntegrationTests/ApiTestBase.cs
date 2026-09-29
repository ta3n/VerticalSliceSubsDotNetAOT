using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SubscriptionManagement.IntegrationTests;

[Collection(ApiCollection.Name)]
public abstract class ApiTestBase(ApiFixture fixture)
{
    protected ApiFixture Fixture { get; } = fixture;
    protected HttpClient Client => Fixture.Client;

    protected static object NewSubscription(
        string name = "Netflix", decimal price = 99000m, string start = "2026-09-05", string? trial = null) =>
        new { serviceName = name, price, startDate = start, trialEndsAt = trial };

    protected async Task<JsonElement> CreateSubscriptionAsync(object? body = null)
    {
        var response = await Client.PostAsJsonAsync("/subscriptions", body ?? NewSubscription());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync(response);
    }

    protected static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    /// <summary>Asserts an RFC 9457 response with the common members and returns the body.</summary>
    protected static async Task<JsonElement> AssertProblemAsync(
        HttpResponseMessage response, HttpStatusCode status, string typeSlug, string? path = null)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await ReadAsync(response);
        Assert.Equal($"https://api.demo/errors/{typeSlug}", body.GetProperty("type").GetString());
        Assert.Equal((int)status, body.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrEmpty(body.GetProperty("traceId").GetString()));
        Assert.True(body.TryGetProperty("timestamp", out _));
        if (path is not null)
        {
            Assert.Equal(path, body.GetProperty("instance").GetString());
        }

        return body;
    }

    protected static string Id(JsonElement element) => element.GetProperty("id").GetString()!;

    protected Task<HttpResponseMessage> PostAsync(string url) => Client.PostAsync(url, content: null);
}
