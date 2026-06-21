namespace Tnzi.AI.Sandbox.Options;

public class SandboxModuleOptions
{
    public string Provider { get; set; } = "local";
    public string DataRoot { get; set; } = ".tnzi-ai/threads";
    public bool Enabled { get; set; } = true;
    public bool LazyDirectoryCreation { get; set; } = true;
    public LocalSandboxOptions Local { get; set; } = new();
    public DockerSandboxOptions Docker { get; set; } = new();
    public KubernetesSandboxOptions Kubernetes { get; set; } = new();
    public ThreadQuotaOptions ThreadQuota { get; set; } = new();
}

/// <summary>
/// Per-thread sandbox resource budget. Caps how much compute and output a
/// single AI thread may consume through <c>SandboxTools.BashAsync</c> within
/// a rolling time window.
/// </summary>
public class ThreadQuotaOptions
{
    /// <summary>Master switch — when false the quota service short-circuits to Allow.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum number of <c>bash</c> invocations per thread within the window.
    /// Set to <c>0</c> to disable the count cap (other caps still apply).
    /// </summary>
    public int MaxCommandCount { get; set; } = 200;

    /// <summary>
    /// Maximum cumulative <c>bash</c> wall-clock duration per thread (milliseconds).
    /// Defaults to 10 minutes. Set to <c>0</c> to disable.
    /// </summary>
    public long MaxTotalDurationMs { get; set; } = 600_000;

    /// <summary>
    /// Maximum combined stdout + stderr bytes a thread may emit. Defaults to
    /// 50 MB. Set to <c>0</c> to disable.
    /// </summary>
    public long MaxTotalOutputBytes { get; set; } = 50L * 1024 * 1024;

    /// <summary>
    /// Rolling window length. Counters silently expire after this duration of
    /// inactivity and reset on the next command. Default 24 hours.
    /// </summary>
    public TimeSpan WindowDuration { get; set; } = TimeSpan.FromHours(24);
}

public class LocalSandboxOptions
{
    public List<string> AllowedDirectories { get; set; } = ["."];

    /// <summary>
    /// Substring patterns matched case-insensitively against the full command.
    /// Backwards-compatible with pre-2026-05-21 behaviour. For token-level
    /// matching that resists quote / whitespace bypass, use
    /// <see cref="DeniedCommandPrefixes"/> instead.
    /// </summary>
    public List<string> DeniedCommands { get; set; } = ["rm -rf /", "format c:", "chmod 777 /", "mkfs"];

    /// <summary>
    /// Binary names whose execution is rejected at every pipeline segment
    /// (e.g. <c>cat foo | mkfs ...</c> is denied because the second segment's
    /// binary is <c>mkfs</c>). Token-level match via <c>IShellCommandAnalyzer</c>;
    /// immune to <c>'rm' -rf /</c> and <c>rm  -rf  /</c> bypass tricks.
    /// </summary>
    public List<string> DeniedCommandPrefixes { get; set; } =
        ["mkfs", "shutdown", "reboot", "halt", "poweroff", "init", "fdisk", "dd"];

    /// <summary>
    /// Wildcard patterns matched (case-insensitive) against the leaf file name in
    /// <c>read_file</c> / <c>ls</c>. A match is hidden from listings and refused on
    /// read so the agent cannot exfiltrate secrets via the file tools. Note this
    /// guards the file tools only — a raw <c>bash cat</c> is governed by the command
    /// blacklist, not this list.
    /// </summary>
    public List<string> DeniedPatterns { get; set; } = [".env", "*.key", "*.pem", "credentials*"];
    public List<string> EnvironmentBlacklist { get; set; } =
        ["API_KEY", "SECRET_KEY", "ACCESS_TOKEN", "PRIVATE_KEY", "OPENAI_API_KEY", "ANTHROPIC_API_KEY"];

    /// <summary>
    /// Opt-in flag to allow LocalSandboxProvider in Production environments.
    /// Default: false. LocalSandbox runs commands directly on the host with only a
    /// substring-based command blacklist, which offers no meaningful isolation and is
    /// intended for development/CI use only. Set this to true only if you fully
    /// understand the risk and have compensating controls at the OS layer.
    /// </summary>
    public bool AllowInProduction { get; set; }
}

public class DockerSandboxOptions
{
    /// <summary>
    /// Docker daemon host URI. Defaults to platform-appropriate socket.
    /// Linux: unix:///var/run/docker.sock, Windows: npipe:////./pipe/docker_engine
    /// </summary>
    public string DockerHost { get; set; } = OperatingSystem.IsWindows()
        ? "npipe:////./pipe/docker_engine"
        : "unix:///var/run/docker.sock";

    /// <summary>
    /// Default container image for sandbox execution. Defaults to the official
    /// <c>tnzi/sandbox:python3.12</c> image (Python 3.12-slim + openpyxl + pandas
    /// + reportlab + weasyprint, runs as non-root uid 1000). Build locally from
    /// <c>docker/sandbox/Dockerfile</c> or pull from a configured registry.
    /// </summary>
    public string Image { get; set; } = "tnzi/sandbox:python3.12";

    /// <summary>
    /// Maximum number of concurrent containers
    /// </summary>
    public int MaxContainers { get; set; } = 5;

    /// <summary>
    /// Container idle timeout in seconds before auto-cleanup
    /// </summary>
    public int IdleTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Container memory limit in MB
    /// </summary>
    public int MemoryLimitMb { get; set; } = 512;

    /// <summary>
    /// Container CPU limit (1.0 = one full CPU core)
    /// </summary>
    public double CpuLimit { get; set; } = 1.0;

    /// <summary>
    /// Enable automatic container cleanup on disposal
    /// </summary>
    public bool AutoRemove { get; set; } = true;

    /// <summary>
    /// Backwards-compatible substring-based command blacklist (case-insensitive).
    /// </summary>
    public List<string> DeniedCommands { get; set; } = ["rm -rf /", "format c:", "chmod 777 /", "mkfs"];

    /// <summary>
    /// Token-level binary blacklist evaluated per pipeline segment via
    /// <c>IShellCommandAnalyzer</c>. Resists quote / whitespace bypass.
    /// </summary>
    public List<string> DeniedCommandPrefixes { get; set; } =
        ["mkfs", "shutdown", "reboot", "halt", "poweroff", "init", "fdisk", "dd"];

    /// <summary>
    /// Wildcard patterns matched (case-insensitive) against the leaf file name in
    /// <c>read_file</c> / <c>ls</c>. A match is hidden from listings and refused on
    /// read so the agent cannot exfiltrate secrets via the file tools. Note this
    /// guards the file tools only — a raw <c>bash cat</c> is governed by the command
    /// blacklist, not this list.
    /// </summary>
    public List<string> DeniedPatterns { get; set; } = [".env", "*.key", "*.pem", "credentials*"];

    /// <summary>
    /// Drop all Linux capabilities inside the container. Defaults to <c>true</c>
    /// (defense-in-depth). Disable only if a specific skill requires elevated
    /// privileges, and prefer narrowing via <see cref="ExtraSecurityOpts"/> instead.
    /// </summary>
    public bool DropAllCapabilities { get; set; } = true;

    /// <summary>
    /// Maximum number of processes/threads inside the container. Mitigates
    /// fork-bomb-style resource exhaustion. <c>0</c> or negative disables the limit.
    /// </summary>
    public int PidsLimit { get; set; } = 128;

    /// <summary>
    /// Make the container rootfs read-only. The bind-mounted <c>/workspace</c>
    /// remains writable because it is a separate volume, not part of the rootfs.
    /// When enabled, a small writable tmpfs is mounted at <c>/tmp</c> so Python
    /// libraries (PIL, weasyprint, fontconfig caches) keep working.
    /// </summary>
    public bool ReadonlyRootfs { get; set; } = true;

    /// <summary>
    /// User to run commands as, formatted as <c>uid[:gid]</c>. Defaults to the
    /// non-root <c>sandbox</c> user baked into <c>tnzi/sandbox:python3.12</c>
    /// (uid 1000). Overrides any <c>USER</c> directive in the image.
    /// </summary>
    public string RunAsUser { get; set; } = "1000:1000";

    /// <summary>
    /// Additional Docker <c>--security-opt</c> values to apply. The framework
    /// always adds <c>no-new-privileges</c>; entries here are appended.
    /// Examples: <c>seccomp=default</c>, <c>apparmor=docker-default</c>.
    /// </summary>
    public List<string> ExtraSecurityOpts { get; set; } = [];
}

public class KubernetesSandboxOptions
{
    public string Namespace { get; set; } = "tnzi-sandbox";
    public string Image { get; set; } = "mcr.microsoft.com/dotnet/sdk:10.0";
    public string? CpuRequest { get; set; }
    public string? MemoryRequest { get; set; }
    public string? CpuLimit { get; set; }
    public string? MemoryLimit { get; set; }
}
