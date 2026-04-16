namespace Tnzi.AI.Sandbox.Providers.Local;

public class LocalSandboxProvider : ISandboxProvider
{
    private readonly IOptions<SandboxModuleOptions> _options;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<LocalSandboxProvider> _logger;
    private int _counter;
    private int _productionWarningIssued;

    public string Name => "local";

    public LocalSandboxProvider(
        IOptions<SandboxModuleOptions> options,
        IHostEnvironment hostEnvironment,
        ILogger<LocalSandboxProvider> logger)
    {
        _options = Check.NotNull(options);
        _hostEnvironment = Check.NotNull(hostEnvironment);
        _logger = Check.NotNull(logger);
    }

    public Task<ISandbox> CreateAsync(SandboxCreateOptions options, CancellationToken ct = default)
    {
        var localOpts = _options.Value.Local;
        var isProduction = _hostEnvironment.IsProduction();

        if (isProduction && !localOpts.AllowInProduction)
        {
            throw new InvalidOperationException(
                "LocalSandboxProvider is disabled in Production environments because it " +
                "offers no process isolation. Configure AI:Sandbox:Provider=docker for " +
                "production, or set AI:Sandbox:Local:AllowInProduction=true to opt in " +
                "after reviewing the security implications.");
        }

        if (isProduction
            && Interlocked.CompareExchange(ref _productionWarningIssued, 1, 0) == 0)
        {
            _logger.LogWarning(
                "LocalSandboxProvider is running in Production with AllowInProduction=true. " +
                "Commands execute directly on the host with no meaningful isolation. " +
                "This is only acceptable if compensating OS-level controls are in place.");
        }

        var id = $"local-{Interlocked.Increment(ref _counter)}";

        ISandbox sandbox = new LocalSandbox(
            id: id,
            workspacePath: options.WorkspacePath,
            commandTimeout: options.CommandTimeout,
            maxOutputSize: options.MaxOutputSizeBytes,
            deniedCommands: localOpts.DeniedCommands,
            environmentBlacklist: options.EnvironmentBlacklist ?? localOpts.EnvironmentBlacklist,
            environmentOverrides: options.EnvironmentOverrides);

        return Task.FromResult(sandbox);
    }
}
