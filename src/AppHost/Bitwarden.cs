using Aspire.Hosting.Dotnet;

namespace AppHost;

/// <summary>
/// Bitwarden Secrets Manager (Community Toolkit). Opt-in with <c>Bitwarden:Enabled=true</c>: a local run needs no
/// Bitwarden account. The three parameters are read from AppHost configuration, e.g. user secrets:
/// <code>
/// dotnet user-secrets --project src/AppHost set Parameters:bitwarden-project "subscription-management"
/// dotnet user-secrets --project src/AppHost set Parameters:bitwarden-organization-id "&lt;organization guid&gt;"
/// dotnet user-secrets --project src/AppHost set Parameters:bitwarden-access-token "&lt;management token&gt;"
/// </code>
/// The management token needs read and write access to the project; it never leaves the AppHost.
/// </summary>
internal static class Bitwarden
{
    public static IResourceBuilder<BitwardenSecretManagerResource> AddBitwarden(
        this IDistributedApplicationBuilder builder
    )
    {
        // A name creates the project, a GUID adopts an existing one (a name does not look up a remote project).
        var project = builder.AddParameter("bitwarden-project");
        var organizationId = builder.AddParameter("bitwarden-organization-id");
        var accessToken = builder.AddParameter("bitwarden-access-token", true);

        return builder.AddBitwardenSecretManager("bitwarden", project, organizationId, accessToken);
    }

    /// <summary>
    /// Gives the project the client settings (<c>Aspire:Bitwarden:SecretManager:bitwarden:*</c>) and holds it back until
    /// the one-shot provisioning of the project and its managed secrets has finished.
    /// </summary>
    public static IResourceBuilder<DotnetProjectResource> WithBitwarden(
        this IResourceBuilder<DotnetProjectResource> project,
        IResourceBuilder<BitwardenSecretManagerResource> bitwarden
    )
    {
        return project.WithReference(bitwarden).WaitForCompletion(bitwarden);
    }
}