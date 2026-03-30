namespace Tnzi.AI.Sandbox.Providers.Kubernetes;

/// <summary>
/// Kubernetes pod sandbox provider. Phase 1: functional stub that falls back to local execution.
/// Full K8s pod lifecycle will use KubernetesClient in a dedicated PR.
/// </summary>
public class KubernetesSandboxProvider : ISandboxProvider
{
    private readonly IOptions<SandboxModuleOptions> _options;
    private readonly ILogger<KubernetesSandboxProvider> _logger;

    public string Name => "kubernetes";

    public KubernetesSandboxProvider(IOptions<SandboxModuleOptions> options, ILogger<KubernetesSandboxProvider> logger)
    {
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    public Task<ISandbox> CreateAsync(SandboxCreateOptions options, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating Kubernetes sandbox for thread {ThreadId} (namespace: {Namespace})",
            options.ThreadId, _options.Value.Kubernetes.Namespace);

        ISandbox sandbox = new KubernetesSandbox(
            id: $"k8s-{options.ThreadId:N}",
            workspacePath: options.WorkspacePath,
            commandTimeout: options.CommandTimeout,
            maxOutputSize: options.MaxOutputSizeBytes,
            logger: _logger);

        return Task.FromResult(sandbox);
    }
}
