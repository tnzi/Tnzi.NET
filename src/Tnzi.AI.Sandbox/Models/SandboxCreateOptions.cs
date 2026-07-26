namespace Tnzi.AI.Sandbox.Models;

public record SandboxCreateOptions
{
    public Guid ThreadId { get; init; }
    public string WorkspacePath { get; init; } = string.Empty;
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(120);
    public long MaxFileSizeBytes { get; init; } = 50 * 1024 * 1024;
    public long MaxOutputSizeBytes { get; init; } = 512 * 1024;
    public IReadOnlyList<string>? EnvironmentWhitelist { get; init; }
    public IReadOnlyList<string>? EnvironmentBlacklist { get; init; }

    /// <summary>
    /// Environment variables to inject or override when spawning sandbox processes.
    /// Applied AFTER whitelist/blacklist filtering - values here are always present
    /// regardless of filter state. Use for deterministic environment setup (e.g.
    /// PYTHONDONTWRITEBYTECODE=1, LANG=C.UTF-8).
    /// Local provider: ProcessStartInfo.Environment. Docker: -e flags. Kubernetes: env field.
    /// </summary>
    public IReadOnlyDictionary<string, string>? EnvironmentOverrides { get; init; }
}
