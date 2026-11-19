using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.Keycloak;

namespace SubscriptionManagement.IntegrationTests;

/// <summary>A real Keycloak with the realm the AppHost imports (<c>src/AppHost/keycloak</c>).</summary>
public sealed class KeycloakFixture : IAsyncLifetime
{
  private readonly KeycloakContainer _keycloak = new KeycloakBuilder("quay.io/keycloak/keycloak:26.4")
    .WithResourceMapping(
      new FileInfo(Path.Combine(AppContext.BaseDirectory, "keycloak", "subscriptions-realm.json")),
      "/opt/keycloak/data/import/"
    )
    .WithCommand("--import-realm")
    .Build();

  public string Authority => $"{_keycloak.GetBaseAddress().TrimEnd('/')}/realms/subscriptions";

  public Task InitializeAsync()
  {
    return _keycloak.StartAsync();
  }

  public async Task DisposeAsync()
  {
    await _keycloak.DisposeAsync();
  }

  /// <summary>An access token like the web app's: the password grant of the realm's <c>loadtest</c> client.</summary>
  public async Task<string> TokenAsync(
    string username
  )
  {
    using var http = new HttpClient();
    var response = await http.PostAsync(
      $"{Authority}/protocol/openid-connect/token",
      new FormUrlEncodedContent(
        new Dictionary<string, string>
        {
          ["grant_type"] = "password",
          ["client_id"] = "loadtest",
          ["client_secret"] = "loadtest-secret",
          ["username"] = username,
          ["password"] = username
        }
      )
    );
    response.EnsureSuccessStatusCode();
    using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return body.RootElement.GetProperty("access_token").GetString()!;
  }
}

public sealed class AuthenticationTests(
  ApiFixture fixture,
  KeycloakFixture keycloak
) : ApiTestBase(fixture), IClassFixture<KeycloakFixture>
{
  private HttpClient SecuredClient(
    WebApplicationFactory<Program> factory,
    string? token
  )
  {
    var client = factory.CreateClient();
    if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    return client;
  }

  private WebApplicationFactory<Program> Secured()
  {
    return Fixture.CreateFactory(
      false,
      settings: new Dictionary<string, string>
      {
        ["Auth:Authority"] = keycloak.Authority,
        ["Auth:Audience"] = "api",
        ["Auth:RequireHttpsMetadata"] = "false"
      }
    );
  }

  private static object NewPlan()
  {
    return new
    {
      code = $"auth-{Guid.NewGuid():N}"[..24], name = "Auth plan", amount = 10m, currency = "USD",
      interval = "MONTH", trialDays = 0
    };
  }

  [Fact]
  public async Task Requests_need_a_valid_token_but_health_stays_anonymous()
  {
    await using var factory = Secured();
    using var anonymous = SecuredClient(factory, null);
    using var forged = SecuredClient(factory, "not.a.token");

    Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/plans")).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, (await forged.GetAsync("/plans")).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/health/live")).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/health/ready")).StatusCode);
  }

  [Fact]
  public async Task A_viewer_may_read_but_not_write()
  {
    await using var factory = Secured();
    using var viewer = SecuredClient(factory, await keycloak.TokenAsync("viewer"));

    Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/plans")).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/plans", NewPlan())).StatusCode);
    // The search is a POST because it takes a body, but it only reads.
    Assert.Equal(HttpStatusCode.OK, (await viewer.PostAsJsonAsync("/subscriptions/search", new { })).StatusCode);
  }

  [Fact]
  public async Task An_admin_may_write()
  {
    await using var factory = Secured();
    using var admin = SecuredClient(factory, await keycloak.TokenAsync("admin"));

    Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/plans", NewPlan())).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/plans")).StatusCode);
  }

  [Fact]
  public async Task Without_an_authority_the_api_stays_open()
  {
    // The shared factory of every other test: no Auth:Authority.
    Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/plans")).StatusCode);
  }
}