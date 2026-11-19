namespace AppHost;

/// <summary>
/// Keycloak as the identity provider (run mode only: realm import is not supported when publishing). The realm
/// <c>subscriptions</c> is imported from <c>keycloak/subscriptions-realm.json</c>: the public client <c>frontend</c>
/// (Authorization Code + PKCE), the audience <c>api</c>, the confidential client <c>loadtest</c> (service account, used
/// by k6) and the users <c>admin</c>/<c>admin</c> (role admin: reads and writes) and <c>viewer</c>/<c>viewer</c> (read-only).
/// Access tokens carry the realm roles in the <c>roles</c> claim. Opt-out with <c>Keycloak:Enabled=false</c>: every
/// service is then open, as before.
/// </summary>
internal static class Identity
{
  public const string Realm = "subscriptions";
  public const string Audience = "api";
  public const string OidcEndpoint = "oidc";

  /// <summary>
  /// No data volume on purpose: everything Keycloak holds comes from the realm import, so each start begins from the
  /// file (a volume would keep the first import and ignore later edits).
  /// </summary>
  /// <remarks>
  /// Aspire serves Keycloak over HTTPS with its developer certificate. The <c>oidc</c> endpoint adds plain HTTP on the
  /// fixed host port 8081, for everything that talks OpenID Connect to it (services, the browser, k6 containers via
  /// host.docker.internal): none of them has to trust a certificate, and the issuer of every token is
  /// <c>http://localhost:8081/realms/subscriptions</c> whichever host the client used. Run without <c>--isolated</c>:
  /// that option randomizes host ports.
  /// </remarks>
  public static IResourceBuilder<KeycloakResource> AddIdentity(
    this IDistributedApplicationBuilder builder
  )
  {
    return builder.AddKeycloak("keycloak")
      .WithRealmImport("./keycloak")
      .WithEndpoint(
        OidcEndpoint,
        endpoint =>
        {
          endpoint.Port = 8081;
          endpoint.TargetPort = 8080;
          endpoint.UriScheme = "http";
        }
      )
      .WithEnvironment("KC_HTTP_ENABLED", "true")
      .WithEnvironment("KC_HTTP_PORT", "8080")
      .WithEnvironment("KC_HOSTNAME", "http://localhost:8081")
      .WithEnvironment("KC_HOSTNAME_STRICT", "false");
  }

  /// <summary>The service validates the realm's access tokens (<c>Auth:*</c>, see ServiceDefaults) and starts after Keycloak.</summary>
  public static IResourceBuilder<T> WithIdentity<T>(
    this IResourceBuilder<T> service,
    IResourceBuilder<KeycloakResource> keycloak
  ) where T : IResourceWithEnvironment, IResourceWithWaitSupport
  {
    return service
      .WithEnvironment(
        "Auth__Authority",
        ReferenceExpression.Create($"{keycloak.GetEndpoint(OidcEndpoint)}/realms/{Realm}")
      )
      .WithEnvironment("Auth__Audience", Audience)
      .WithEnvironment("Auth__RequireHttpsMetadata", "false")
      .WaitFor(keycloak);
  }
}