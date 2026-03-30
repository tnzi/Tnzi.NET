namespace Tnzi.AI.Sandbox.Providers.Local;

public class LocalSandboxProvider : ISandboxProvider
{
    private readonly IOptions<SandboxModuleOptions> _options;
    private int _counter;

    public string Name => "local";

    public LocalSandboxProvider(IOptions<SandboxModuleOptions> options)
    {
        _options = Check.NotNull(options);
    }

    public Task<ISandbox> CreateAsync(SandboxCreateOptions options, CancellationToken ct = default)
    {
        var id = $"local-{Interlocked.Increment(ref _counter)}";
        var localOpts = _options.Value.Local;

        ISandbox sandbox = new LocalSandbox(
            id: id,
            workspacePath: options.WorkspacePath,
            commandTimeout: options.CommandTimeout,
            maxOutputSize: options.MaxOutputSizeBytes,
            deniedCommands: localOpts.DeniedCommands,
            environmentBlacklist: options.EnvironmentBlacklist ?? localOpts.EnvironmentBlacklist);

        return Task.FromResult(sandbox);
    }
}
