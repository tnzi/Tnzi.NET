namespace Tnzi.AI.Coder.Shell;

/// <summary>
/// Shell 适配器 — 抽象不同操作系统上的 shell 进程启动与参数转义。
/// </summary>
public interface IShellAdapter
{
    /// <summary>
    /// Shell 名称（如 powershell / bash）。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Shell 可执行文件路径。
    /// </summary>
    string ExecutablePath { get; }

    /// <summary>
    /// 创建执行 shell 命令的 <see cref="ProcessStartInfo"/>。
    /// </summary>
    ProcessStartInfo CreateProcessStartInfo(
        string command,
        string workingDirectory,
        bool redirectStandardOutput = true,
        bool redirectStandardError = true);

    /// <summary>
    /// 对 shell 参数做安全引用，避免空格或特殊字符破坏命令结构。
    /// </summary>
    string QuoteArgument(string value);
}

/// <summary>
/// Shell 适配器基类。
/// </summary>
public abstract class ShellAdapterBase : IShellAdapter
{
    public abstract string Name { get; }

    public abstract string ExecutablePath { get; }

    public ProcessStartInfo CreateProcessStartInfo(
        string command,
        string workingDirectory,
        bool redirectStandardOutput = true,
        bool redirectStandardError = true)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = redirectStandardOutput,
            RedirectStandardError = redirectStandardError,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        ConfigureArguments(psi, command);
        return psi;
    }

    public abstract string QuoteArgument(string value);

    protected abstract void ConfigureArguments(ProcessStartInfo psi, string command);
}

/// <summary>
/// Bash shell 适配器。
/// </summary>
public sealed class BashShellAdapter : ShellAdapterBase
{
    public const string DefaultExecutablePath = "/bin/bash";

    public override string Name => "bash";

    public override string ExecutablePath => DefaultExecutablePath;

    public override string QuoteArgument(string value)
    {
        var escaped = value.Replace("'", "'\\''", StringComparison.Ordinal);
        return $"'{escaped}'";
    }

    protected override void ConfigureArguments(ProcessStartInfo psi, string command)
    {
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(command);
    }
}

/// <summary>
/// PowerShell shell 适配器 — 自动检测 pwsh (PowerShell Core) 或 powershell.exe (Windows PowerShell)。
/// </summary>
/// <remarks>
/// 优先使用 pwsh（跨平台 PowerShell 7+），不可用时回退到 powershell.exe（仅 Windows）。
/// 启动参数包含 -NoLogo -NoProfile -NonInteractive 以减少启动开销和避免用户 profile 副作用。
/// </remarks>
public sealed class PowerShellShellAdapter : ShellAdapterBase
{
    /// <summary>
    /// Windows PowerShell 可执行文件路径（仅 Windows）。
    /// </summary>
    public const string WindowsPowerShellPath = "powershell.exe";

    /// <summary>
    /// PowerShell Core 可执行文件名（跨平台）。
    /// </summary>
    public const string PwshPath = "pwsh";

    /// <summary>
    /// 向后兼容：默认可执行路径。
    /// </summary>
    public const string DefaultExecutablePath = WindowsPowerShellPath;

    private readonly string _resolvedPath;

    public PowerShellShellAdapter()
    {
        _resolvedPath = ResolvePowerShellPath();
    }

    /// <summary>
    /// 使用指定的可执行文件路径创建适配器（用于测试或自定义路径场景）。
    /// </summary>
    public PowerShellShellAdapter(string executablePath)
    {
        _resolvedPath = Check.NotNullOrWhiteSpace(executablePath);
    }

    /// <summary>
    /// 是否使用 PowerShell Core (pwsh)。
    /// </summary>
    public bool IsPwsh => _resolvedPath.Contains("pwsh", StringComparison.OrdinalIgnoreCase);

    public override string Name => IsPwsh ? "pwsh" : "powershell";

    public override string ExecutablePath => _resolvedPath;

    public override string QuoteArgument(string value)
    {
        // PowerShell 中单引号内的单引号用双写转义
        var escaped = value.Replace("'", "''", StringComparison.Ordinal);
        return $"'{escaped}'";
    }

    protected override void ConfigureArguments(ProcessStartInfo psi, string command)
    {
        psi.ArgumentList.Add("-NoLogo");
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(command);
    }

    /// <summary>
    /// 检测 PowerShell 可执行文件路径：优先 pwsh，回退 powershell.exe。
    /// </summary>
    private static string ResolvePowerShellPath()
    {
        // 优先使用 PowerShell Core（pwsh）— 跨平台、性能更好
        if (IsPwshAvailable())
        {
            return PwshPath;
        }

        // Windows 回退到内置 powershell.exe
        if (OperatingSystem.IsWindows())
        {
            return WindowsPowerShellPath;
        }

        // 非 Windows 且无 pwsh，仍返回 pwsh 让执行时报明确错误
        return PwshPath;
    }

    /// <summary>
    /// 检查 pwsh 是否在 PATH 中可用。
    /// </summary>
    private static bool IsPwshAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = PwshPath,
                ArgumentList = { "--version" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            process.WaitForExit(3000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
