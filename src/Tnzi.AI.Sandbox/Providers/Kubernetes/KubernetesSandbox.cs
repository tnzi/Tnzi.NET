namespace Tnzi.AI.Sandbox.Providers.Kubernetes;

/// <summary>
/// Kubernetes pod sandbox. Phase 1: delegates to LocalSandbox.
/// Full implementation will create pods and exec via KubernetesClient.
/// </summary>
public class KubernetesSandbox : ISandbox
{
    private readonly LocalSandbox _local;
    private readonly ILogger _logger;

    public string Id { get; }

    public KubernetesSandbox(string id, string workspacePath, TimeSpan commandTimeout,
        long maxOutputSize, ILogger logger)
    {
        Id = Check.NotNullOrWhiteSpace(id);
        _logger = Check.NotNull(logger);
        _local = new LocalSandbox(id, workspacePath, commandTimeout, maxOutputSize);
    }

    public Task<CommandResult> ExecuteCommandAsync(string command, CancellationToken ct = default)
    {
        // TODO: Execute via kubectl exec in pod
        _logger.LogWarning("Kubernetes sandbox executing locally (pod support pending)");
        return _local.ExecuteCommandAsync(command, ct);
    }

    public Task<string> ReadFileAsync(string path, CancellationToken ct = default)
        => _local.ReadFileAsync(path, ct);

    public Task WriteFileAsync(string path, string content, bool append = false, CancellationToken ct = default)
        => _local.WriteFileAsync(path, content, append, ct);

    public Task UpdateFileAsync(string path, byte[] content, CancellationToken ct = default)
        => _local.UpdateFileAsync(path, content, ct);

    public Task<IReadOnlyList<FileEntry>> ListDirectoryAsync(string path, int maxDepth = 2, CancellationToken ct = default)
        => _local.ListDirectoryAsync(path, maxDepth, ct);

    public ValueTask DisposeAsync()
    {
        // TODO: Delete K8s pod
        _logger.LogDebug("Kubernetes sandbox {Id} disposed", Id);
        return ValueTask.CompletedTask;
    }
}
