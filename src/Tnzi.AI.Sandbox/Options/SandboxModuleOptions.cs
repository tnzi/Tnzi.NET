namespace Tnzi.AI.Sandbox.Options;

public class SandboxModuleOptions
{
    public string Provider { get; set; } = "local";

    /// <summary>
    /// 线程数据根目录：每个用到沙箱的线程在 <c>{DataRoot}/{ThreadId:N}/</c> 下有 workspace / uploads /
    /// outputs / skills 四个子目录。相对路径按进程当前目录解析。
    /// </summary>
    /// <remarks>
    /// ★ 默认值是<b>应用目录之外</b>的 <see cref="DefaultDataRoot"/>（<c>{LocalApplicationData}/Tnzi/ai-threads</c>），
    /// 2026-09-14 之前是内容根下的相对路径 <c>.tnzi-ai/threads</c>。运行期数据落在部署目录里，对把发布目录
    /// 当作一个整体同步到生产主机的部署是一个事故点：某消费方一天 863 次运行留下的 92,000 个文件把一个
    /// 62 字节的 <c>app_offline</c> 标记的送达拖到了 24 分钟。显式配置的值原样保留；配到内容根之下时
    /// 启动期记一条 Warning（<see cref="SandboxModuleOptionsValidator"/>）。
    /// </remarks>
    public string DataRoot { get; set; } = DefaultDataRoot;

    public bool Enabled { get; set; } = true;
    public bool LazyDirectoryCreation { get; set; } = true;
    public LocalSandboxOptions Local { get; set; } = new();
    public DockerSandboxOptions Docker { get; set; } = new();
    public KubernetesSandboxOptions Kubernetes { get; set; } = new();
    public ThreadQuotaOptions ThreadQuota { get; set; } = new();

    /// <summary>
    /// 未显式配置 <see cref="DataRoot"/> 时的落点：<c>{LocalApplicationData}/Tnzi/ai-threads</c>，与
    /// <c>Tnzi.AI.Cli</c> 的工作区根 <c>{LocalApplicationData}/Tnzi/agent-workspaces</c> 同一父目录。
    /// 宿主解析不出用户数据目录（没有 HOME 的服务账号、IIS 未加载用户配置文件）时为空串，
    /// 沙箱启用时由验证器拒绝启动并指名要配 <c>AI:Sandbox:DataRoot</c>；不再退回任何相对路径，那正是要离开的地方。
    /// </summary>
    public static string DefaultDataRoot { get; } = ResolveDefaultDataRoot(Environment.GetFolderPath);

    /// <remarks>
    /// ★ 必须 <see cref="Environment.SpecialFolderOption.DoNotVerify"/>：默认的 <c>None</c> 在 Unix 上对<b>尚不存在</b>的目录
    /// 返回空串（macOS 与新建的 Linux 账号上 <c>~/.local/share</c> 往往还没建），于是一个完全正常的宿主被当成
    /// 「解析不出用户数据目录」拒绝启动。目录不存在不是问题，沙箱第一次布置线程目录时会逐级建出来。
    /// </remarks>
    internal static string ResolveDefaultDataRoot(Func<Environment.SpecialFolder, Environment.SpecialFolderOption, string> getFolderPath)
    {
        var localAppData = getFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        return string.IsNullOrWhiteSpace(localAppData)
            ? string.Empty
            : Path.Combine(localAppData, "Tnzi", "ai-threads");
    }
}

/// <summary>
/// Per-thread sandbox resource budget. Caps how much compute and output a
/// single AI thread may consume through <c>SandboxTools.BashAsync</c> within
/// a rolling time window.
/// </summary>
[ConfigSection("AI:Sandbox:ThreadQuota")]
[RuntimeSettingGroup(Key = "ai-sandbox", Module = "AI", DisplayName = "Sandbox",
    I18nKey = "admin.modules.system.settings.groups.aiSandbox",
    Icon = "mdi:cube-outline", Order = 157)]
public class ThreadQuotaOptions
{
    /// <summary>Master switch - when false the quota service short-circuits to Allow.</summary>
    [RuntimeSetting(Label = "Thread Quota Enabled", I18n = "admin.modules.system.settings.fields.sandboxQuotaEnabled",
        Type = SettingFieldType.Boolean, Subsection = "Thread Quota",
        Description = "Master switch for per-thread sandbox resource limits. When off, every bash invocation is allowed and no accounting is recorded.")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum number of <c>bash</c> invocations per thread within the window.
    /// Set to <c>0</c> to disable the count cap (other caps still apply).
    /// </summary>
    [RuntimeSetting(Label = "Max Command Count", I18n = "admin.modules.system.settings.fields.sandboxQuotaMaxCommandCount",
        Type = SettingFieldType.Int, Min = 0, Subsection = "Thread Quota",
        Description = "Maximum bash invocations per thread within the rolling window (hard cap). 0 disables the count cap; other caps still apply.")]
    public int MaxCommandCount { get; set; } = 200;

    /// <summary>
    /// Maximum cumulative <c>bash</c> wall-clock duration per thread (milliseconds).
    /// Defaults to 10 minutes. Set to <c>0</c> to disable.
    /// </summary>
    [RuntimeSetting(Label = "Max Total Duration (ms)", I18n = "admin.modules.system.settings.fields.sandboxQuotaMaxTotalDurationMs",
        Type = SettingFieldType.Int, Min = 0, Subsection = "Thread Quota",
        Description = "Maximum cumulative bash wall-clock duration per thread in milliseconds (soft cap). 0 disables it. Default 600000 (10 minutes).")]
    public long MaxTotalDurationMs { get; set; } = 600_000;

    /// <summary>
    /// Maximum combined stdout + stderr bytes a thread may emit. Defaults to
    /// 50 MB. Set to <c>0</c> to disable.
    /// </summary>
    [RuntimeSetting(Label = "Max Total Output (bytes)", I18n = "admin.modules.system.settings.fields.sandboxQuotaMaxTotalOutputBytes",
        Type = SettingFieldType.Int, Min = 0, Subsection = "Thread Quota",
        Description = "Maximum combined stdout + stderr bytes a thread may emit (soft cap). 0 disables it. Default 52428800 (50 MB).")]
    public long MaxTotalOutputBytes { get; set; } = 50L * 1024 * 1024;

    /// <summary>
    /// Rolling window length. Counters silently expire after this duration of
    /// inactivity and reset on the next command. Default 24 hours.
    /// </summary>
    [RuntimeSetting(Label = "Window Duration", I18n = "admin.modules.system.settings.fields.sandboxQuotaWindowDuration",
        Type = SettingFieldType.Duration, Subsection = "Thread Quota",
        Description = "Rolling window length (e.g. 1.00:00:00 for 24h). Counters expire after this duration of inactivity and reset on the next command.")]
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
    /// guards the file tools only - a raw <c>bash cat</c> is governed by the command
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
    /// guards the file tools only - a raw <c>bash cat</c> is governed by the command
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
