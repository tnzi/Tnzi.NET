namespace Tnzi.AI.Sandbox.Providers.Docker;

/// <summary>
/// Docker container sandbox provider. Phase 1: functional stub that falls back to local execution.
/// Full container lifecycle will use Docker.DotNet in a dedicated PR.
/// </summary>
public class DockerSandboxProvider : ISandboxProvider
{
    private readonly IOptions<SandboxModuleOptions> _options;
    private readonly ILogger<DockerSandboxProvider> _logger;

    public string Name => "docker";

    public DockerSandboxProvider(IOptions<SandboxModuleOptions> options, ILogger<DockerSandboxProvider> logger)
    {
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    public Task<ISandbox> CreateAsync(SandboxCreateOptions options, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating Docker sandbox for thread {ThreadId} (image: {Image})",
            options.ThreadId, _options.Value.Docker.Image);

        ISandbox sandbox = new DockerSandbox(
            id: $"docker-{options.ThreadId:N}",
            workspacePath: options.WorkspacePath,
            commandTimeout: options.CommandTimeout,
            maxOutputSize: options.MaxOutputSizeBytes,
            image: _options.Value.Docker.Image,
            logger: _logger);

        return Task.FromResult(sandbox);
    }
}
