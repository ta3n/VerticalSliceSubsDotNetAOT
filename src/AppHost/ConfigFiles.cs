using System.Text.RegularExpressions;
using Aspire.Hosting.Kubernetes;
using Aspire.Hosting.Kubernetes.Resources;

namespace AppHost;

/// <summary>
/// The containers read config files and folders that sit next to the AppHost (<c>postgres/</c>, <c>observability/</c>,
/// <c>scripts/</c>), some mounted by a toolkit rather than by this code. <c>aspire run</c> bind-mounts them read-only;
/// the Kubernetes publisher has no bind mounts, so <c>aspire publish</c> ships the same files as a ConfigMap mounted at
/// the same path.
/// </summary>
internal static partial class ConfigFiles
{
    /// <summary>Publish mode only: every bind mount of every container becomes a ConfigMap volume.</summary>
    public static void PublishBindMountsAsConfigMaps(
        this IDistributedApplicationBuilder builder
    )
    {
        if (!builder.ExecutionContext.IsPublishMode)
            return;

        foreach (var container in builder.Resources.OfType<ContainerResource>().ToList())
        {
            var mounts = container.Annotations.OfType<ContainerMountAnnotation>()
                .Where(mount => mount.Type == ContainerMountType.BindMount)
                .ToList();
            foreach (var mount in mounts)
            {
                container.Annotations.Remove(mount);
                var (path, target) = (mount.Source!, mount.Target);
                builder.CreateResourceBuilder(container)
                    .PublishAsKubernetesService(
                        kubernetes => AddConfigMapVolume(kubernetes, container.Name, path, target)
                    );
            }
        }
    }

    private static void AddConfigMapVolume(
        KubernetesResource kubernetes,
        string resourceName,
        string path,
        string target
    )
    {
        // One ConfigMap per mount, not per container: Grafana mounts two folders.
        var name = $"{resourceName}-{NameUnsafe().Replace(target.Trim('/'), "-").ToLowerInvariant()}";
        var isFile = File.Exists(path);
        var root = isFile ? Path.GetDirectoryName(path)! : path;
        var files = isFile ? [path] : Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories);

        // ConfigMap keys have no "/": a nested file travels as "a__b" and the volume item puts it back at "a/b".
        var configMap = new ConfigMap { Metadata = { Name = name } };
        var source = new ConfigMapVolumeSourceV1 { Name = name };
        foreach (var file in files.Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var key = relative.Replace("/", "__");
            // The chart is a Helm template: a literal "{{" (Grafana legends, Prometheus templates) must be escaped (a raw string: YAML would escape the quotes of "{{").
            configMap.Data[key] = File.ReadAllText(file).Replace("{{", "{{ `{{` }}");
            source.Items.Add(new() { Key = key, Path = relative });
        }

        kubernetes.AdditionalResources.Add(configMap);

        var pod = kubernetes.Workload!.PodTemplate.Spec;
        pod.Volumes.Add(new() { Name = name, ConfigMap = source });
        var mount = new VolumeMountV1 { Name = name, MountPath = target, ReadOnly = true };
        if (isFile)
            mount.SubPath = Path.GetFileName(path);
        pod.Containers[0].VolumeMounts.Add(mount);
    }

    [GeneratedRegex("[^A-Za-z0-9]+")]
    private static partial Regex NameUnsafe();
}