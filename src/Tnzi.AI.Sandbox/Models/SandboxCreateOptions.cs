namespace Tnzi.AI.Sandbox.Models;

/// <remarks>
/// 框架自己的构造点（<c>SandboxMiddleware</c>）只给 <see cref="ThreadId"/> 与 <see cref="WorkspacePath"/>：
/// 命令超时、输出上限与文件上限在 agent 运行里就是这里的默认值，没有对应的配置项。直接调用
/// <see cref="ISandboxProvider.CreateAsync"/> 的代码可以自己给。
/// </remarks>
public record SandboxCreateOptions
{
    public Guid ThreadId { get; init; }
    public string WorkspacePath { get; init; } = string.Empty;
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(120);
    public long MaxFileSizeBytes { get; init; } = 50 * 1024 * 1024;
    public long MaxOutputSizeBytes { get; init; } = 512 * 1024;

    /// <summary>
    /// Local provider: when set (an empty list included), the process inherits only the host environment
    /// variables named here, before the blacklist and the overrides apply. <c>null</c> keeps the whole host
    /// environment. The Docker and Kubernetes providers never pass the host environment on, so there it is
    /// satisfied by construction.
    /// </summary>
    public IReadOnlyList<string>? EnvironmentWhitelist { get; init; }

    /// <summary>
    /// Local provider: host environment variables whose names contain one of these substrings are removed.
    /// <c>null</c> uses <c>AI:Sandbox:Local:EnvironmentBlacklist</c>.
    /// </summary>
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
