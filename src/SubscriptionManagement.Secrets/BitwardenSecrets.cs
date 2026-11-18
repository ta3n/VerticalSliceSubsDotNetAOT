using Bitwarden.Sdk;
using CommunityToolkit.Aspire.Bitwarden.SecretManager;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace SubscriptionManagement.Secrets;

/// <summary>
/// Bitwarden Secrets Manager for the JIT services. Does nothing unless the AppHost injected the client settings
/// (<c>Aspire:Bitwarden:SecretManager:bitwarden:*</c>, only with <c>Bitwarden:Enabled=true</c>). Otherwise it registers
/// Aspire's <see cref="BitwardenClient" /> (health check included) and loads every secret of the project into
/// <see cref="IConfiguration" />: the secret key is the configuration key, with <c>__</c> read as <c>:</c> (so a secret
/// named <c>ConnectionStrings__Postgres</c> overrides <c>ConnectionStrings:Postgres</c>).
/// </summary>
public static class BitwardenSecrets
{
    public const string ConnectionName = "bitwarden";

    public static void AddBitwardenSecrets<TBuilder>(
        this TBuilder builder
    ) where TBuilder : IHostApplicationBuilder
    {
        var section = builder.Configuration.GetSection($"Aspire:Bitwarden:SecretManager:{ConnectionName}");
        if (!section.Exists()) return;

        // Secrets are config, so they must be in place before the services read it; the client Aspire registers is for
        // the app (health check, later reads) and this one is only used, and disposed, for the initial load.
        var settings = section.Get<BitwardenSecretManagerClientSettings>()
                       ?? throw new InvalidOperationException($"Aspire:Bitwarden:SecretManager:{ConnectionName} is not valid.");
        builder.Configuration.AddInMemoryCollection(Load(settings));

        builder.AddBitwardenSecretManagerClient(ConnectionName);
    }

    private static Dictionary<string, string?> Load(
        BitwardenSecretManagerClientSettings settings
    )
    {
        using var client = new BitwardenClient(
            new BitwardenSettings { ApiUrl = settings.ApiUrl, IdentityUrl = settings.IdentityUrl }
        );
        client.Auth.LoginAccessToken(settings.AccessToken);

        var sync = client.Secrets.Sync(settings.OrganizationId, null);
        return sync.Secrets
            .Where(secret => secret.ProjectId == settings.ProjectId)
            .ToDictionary(secret => secret.Key.Replace("__", ConfigurationPath.KeyDelimiter), secret => (string?)secret.Value);
    }
}
