namespace Tnzi.AI.Sandbox.Providers.Docker;

/// <summary>
/// Docker container sandbox. Phase 1: delegates to LocalSandbox.
/// Full implementation will execute commands inside Docker containers via Docker.DotNet.
/// </summary>
public class DockerSandbox : ISandbox
{
    private readonly LocalSandbox _local;
    private readonly ILogger _logger;

    public string Id { get; }

    public DockerSandbox(string id, string workspacePath, TimeSpan commandTimeout,
        long maxOutputSize, string image, ILogger logger)
    {
        Id = Check.NotNullOrWhiteSpace(id);
        Check.NotNullOrWhiteSpace(image);
        _logger = Check.NotNull(logger);
        _local = new LocalSandbox(id, workspacePath, commandTimeout, maxOutputSize);
    }

    public Task<CommandResult> ExecuteCommandAsync(string command, CancellationToken ct = default)
    {
        // TODO: Execute via docker exec in container
        _logger.LogWarning("Docker sandbox executing locally (container support pending)");
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
        // TODO: Stop and remove Docker container
        _logger.LogDebug("Docker sandbox {Id} disposed", Id);
        return ValueTask.CompletedTask;
    }
}
