namespace Tnzi.AI.Sandbox.Providers.Kubernetes;

/// <summary>
/// Kubernetes pod sandbox provider (not yet implemented).
/// CreateAsync fails fast with NotSupportedException to prevent silent fall-through
/// to host-local execution that would break the operator's isolation expectations.
/// A full KubernetesClient-based implementation is planned for a dedicated PR.
/// </summary>
public class KubernetesSandboxProvider : ISandboxProvider
{
    private readonly ILogger<KubernetesSandboxProvider> _logger;

    public string Name => "kubernetes";

    public KubernetesSandboxProvider(ILogger<KubernetesSandboxProvider> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public Task<ISandbox> CreateAsync(SandboxCreateOptions options, CancellationToken ct = default)
    {
        _logger.LogError(
            "Kubernetes sandbox provider is not implemented. Configure AI:Sandbox:Provider=local or docker.");
        throw new NotSupportedException(
            "Kubernetes sandbox provider is not implemented. The previous behavior of silently " +
            "falling back to local host execution has been removed because it violated the " +
            "operator's isolation expectations. Configure AI:Sandbox:Provider=local (dev) " +
            "or docker (production) until native K8s pod support ships.");
    }
}
