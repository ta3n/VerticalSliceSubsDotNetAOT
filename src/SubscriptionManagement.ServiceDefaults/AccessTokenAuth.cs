using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Access-token (JWT bearer) authentication against the identity provider, Keycloak under the AppHost. Off unless
/// <c>Auth:Authority</c> is set, so a service without it (tests, a plain <c>dotnet run</c>) stays open.
/// </summary>
/// <remarks>
/// When on: every endpoint needs a valid token (the fallback policy) except those marked <c>AllowAnonymous</c> (health
/// probes, API documentation, gRPC between services), and a request that is not a read (anything but GET, HEAD and
/// OPTIONS, or an endpoint marked <see cref="AsReadOnlyRequest{TBuilder}" />) also needs the role
/// <see cref="AdminRole" />, taken from the token's <c>roles</c> claim.
/// </remarks>
public static class AccessTokenAuth
{
  public const string AdminRole = "admin";

  /// <summary>Marks an endpoint that reads but needs a body, so a viewer may call it with POST.</summary>
  public static TBuilder AsReadOnlyRequest<TBuilder>(
    this TBuilder endpoint
  ) where TBuilder : IEndpointConventionBuilder
  {
    return endpoint.WithMetadata(new ReadOnlyRequest());
  }

  /// <summary>Settings: <c>Auth:Authority</c> (issuer URL), <c>Auth:Audience</c>, <c>Auth:RequireHttpsMetadata</c> (default true).</summary>
  public static TBuilder AddAccessTokenAuth<TBuilder>(
    this TBuilder builder
  ) where TBuilder : IHostApplicationBuilder
  {
    var authority = builder.Configuration["Auth:Authority"];
    if (string.IsNullOrWhiteSpace(authority)) return builder;

    builder.Services.AddSingleton<Enabled>();
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
      .AddJwtBearer(
        options =>
        {
          options.Authority = authority;
          options.Audience = builder.Configuration["Auth:Audience"];
          options.RequireHttpsMetadata = !bool.TryParse(
              builder.Configuration["Auth:RequireHttpsMetadata"],
              out var requireHttps
            )
            || requireHttps;
          // Claims keep the names of the token: "roles" and "preferred_username" as Keycloak writes them.
          options.MapInboundClaims = false;
          options.TokenValidationParameters.RoleClaimType = "roles";
          options.TokenValidationParameters.NameClaimType = "preferred_username";
        }
      );
    builder.Services.AddAuthorizationBuilder()
      .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
    return builder;
  }

  /// <summary>After the exception handler and before the endpoints; does nothing when authentication is off.</summary>
  public static WebApplication UseAccessTokenAuth(
    this WebApplication app
  )
  {
    if (app.Services.GetService<Enabled>() is null) return app;

    app.UseAuthentication();
    app.UseAuthorization();
    app.Use(
      (context, next) =>
      {
        var request = context.Request;
        var isRead = HttpMethods.IsGet(request.Method)
          || HttpMethods.IsHead(request.Method)
          || HttpMethods.IsOptions(request.Method);
        var metadata = context.GetEndpoint()?.Metadata;
        var anonymous = metadata?.GetMetadata<IAllowAnonymous>() is not null;
        var readOnly = metadata?.GetMetadata<ReadOnlyRequest>() is not null;
        if (anonymous
          || (context.User.Identity?.IsAuthenticated == true
            && (isRead || readOnly || context.User.IsInRole(AdminRole))))
          return next(context);

        // Authenticated (the fallback policy ran), but a viewer may only read.
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
      }
    );
    return app;
  }

  /// <summary>Endpoint metadata: the request only reads, although its method is not GET (a search with a body).</summary>
  private sealed class ReadOnlyRequest;

  /// <summary>Registered when authentication is on; <see cref="UseAccessTokenAuth" /> looks for it.</summary>
  private sealed class Enabled;
}