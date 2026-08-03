namespace Tnzi.AI.Cli.Hosting;

/// <summary>
/// 构造子进程环境变量：安全基线 + 显式白名单 + 显式设置项。
/// </summary>
/// <remarks>
/// <para>
/// <b>默认不透传宿主环境</b>。外部 agent 能执行任意命令，把应用进程的完整环境交给它，
/// 等于把数据库连接串、签名密钥、云凭据一并交出去 —— 而 agent 只需要它自己那个
/// provider 的 API key。上一代实现在这一点上做对了，本实现直接沿用。
/// </para>
/// <para>
/// 基线只包含「不给就跑不起来」的那些：可执行文件查找路径、家目录、临时目录、
/// 语言区域、Windows 的系统目录。它们不携带应用机密。
/// </para>
/// </remarks>
public static class CliEnvironmentBuilder
{
    /// <summary>
    /// 安全基线变量名。缺了它们，被启动的 CLI 连自己的运行时都找不到。
    /// </summary>
    public static IReadOnlySet<string> SafeBaselineKeys { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // 跨平台
        "PATH", "HOME", "LANG", "LC_ALL", "LC_CTYPE", "TZ", "TERM", "SHELL",
        "TEMP", "TMP", "TMPDIR", "USER", "LOGNAME",
        // Windows
        "USERNAME", "USERPROFILE", "SystemRoot", "SystemDrive", "windir",
        "ComSpec", "PATHEXT", "APPDATA", "LOCALAPPDATA", "ProgramData",
        "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "CommonProgramFiles",
        "NUMBER_OF_PROCESSORS", "OS", "PROCESSOR_ARCHITECTURE", "PROCESSOR_IDENTIFIER",
        // XDG（Linux CLI 常用来定位自己的配置/缓存）
        "XDG_CONFIG_HOME", "XDG_CACHE_HOME", "XDG_DATA_HOME", "XDG_RUNTIME_DIR"
    };

    /// <summary>
    /// 按规则构造子进程环境。
    /// </summary>
    /// <param name="spec">进程规格（携带白名单与显式设置项）。</param>
    /// <returns>最终生效的环境变量集合。</returns>
    public static Dictionary<string, string> Build(CliProcessSpec spec)
    {
        Check.NotNull(spec);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var host = Environment.GetEnvironmentVariables();

        foreach (System.Collections.DictionaryEntry entry in host)
        {
            if (entry.Key is not string key || entry.Value is not string value)
            {
                continue;
            }

            var allowed = spec.InheritAllHostEnvironment
                          || SafeBaselineKeys.Contains(key)
                          || spec.EnvironmentWhitelist.Contains(key, StringComparer.OrdinalIgnoreCase);

            if (allowed)
            {
                result[key] = value;
            }
        }

        // 显式设置项最后写入，可覆盖继承来的同名变量。
        foreach (var (key, value) in spec.Environment)
        {
            result[key] = value;
        }

        return result;
    }
}
