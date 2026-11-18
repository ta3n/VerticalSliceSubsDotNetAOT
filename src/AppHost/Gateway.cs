using Aspire.Hosting.Dotnet;
using Aspire.Hosting.Yarp;
using Aspire.Hosting.Yarp.Transforms;

namespace AppHost;

internal static class Gateway
{
    /// <summary>
    /// A YARP reverse proxy as the single local entry point: <c>/api/*</c> goes to the API with the prefix stripped,
    /// exactly like the Vite dev proxy, so clients use the same URLs through either. Service discovery resolves the API
    /// endpoint, so all its replicas stay behind Aspire's endpoint proxy.
    /// </summary>
    public static IResourceBuilder<YarpResource> AddGateway(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<DotnetProjectResource> api
    )
    {
        return builder.AddYarp("gateway")
            .WithHostPort(8080)
            .WithConfiguration(
                yarp => yarp.AddRoute("/api/{**catch-all}", api).WithTransformPathRemovePrefix("/api")
            );
    }
}
