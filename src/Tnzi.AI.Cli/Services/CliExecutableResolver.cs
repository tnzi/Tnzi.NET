namespace Tnzi.AI.Cli.Services;

/// <summary>
/// 在 API 进程所在的机器上定位 provider 的可执行文件（本机宿主 <see cref="LocalProcessHost"/> 用它）。
/// </summary>
/// <remarks>
/// 只管本机：远程执行目标在自己那里找（<see cref="ICliHostTarget.FindExecutableAsync"/>）。
/// 版本探测不在这里，它是一条短命令，经 <see cref="CliHostCommand.DetectVersionAsync"/> 在任一目标上运行。
/// </remarks>
public static class CliExecutableResolver
{
    /// <summary>宿主没有 <c>PATHEXT</c> 时按 Windows 的默认值试。</summary>
    private const string DefaultPathExt = ".COM;.EXE;.BAT;.CMD";

    /// <summary>
    /// 解析绝对路径。找不到返回 null。
    /// </summary>
    /// <remarks>
    /// 顺序：配置里的显式路径 → PATH 查找默认名（Windows 上按 PATHEXT 逐个后缀试，不接受无扩展名的文件）。
    /// </remarks>
    public static string? Resolve(CliProviderDescriptor provider)
    {
        Check.NotNull(provider);

        if (!string.IsNullOrWhiteSpace(provider.ExecutablePathOverride))
        {
            return File.Exists(provider.ExecutablePathOverride) ? provider.ExecutablePathOverride : null;
        }

        return SearchPath(
            provider.DefaultExecutable,
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("PATHEXT"),
            OperatingSystem.IsWindows());
    }

    /// <summary>
    /// 在 PATH 上查找可执行文件。
    /// </summary>
    /// <remarks>
    /// Windows 上必须按 <c>PATHEXT</c> 逐个后缀试：很多 CLI 是通过 npm 安装的，
    /// 落在 PATH 里的是 <c>claude.cmd</c> 而不是 <c>claude</c>，只找裸名会一无所获。
    /// <para>
    /// ★ 而且<b>不能先试裸名</b>：npm 在 Windows 上全局安装时一次写三个垫片，<c>qwen</c>（无扩展名的 sh 脚本，
    /// 给 Git Bash 用）、<c>qwen.cmd</c>、<c>qwen.ps1</c>。先试裸名就命中那个 sh 脚本，<c>CreateProcess</c> 起不了它
    /// （不是有效的 Win32 程序），于是探测照样登记 Online、每次运行都以 LaunchFailed 结束。
    /// Windows 上无扩展名的文件一律不是候选；名字本身已带 <c>PATHEXT</c> 里的后缀（如 <c>node.exe</c>）时才原样试。
    /// </para>
    /// </remarks>
    internal static string? SearchPath(string executableName, string? pathValue, string? pathExt, bool isWindows)
    {
        if (string.IsNullOrWhiteSpace(executableName))
        {
            return null;
        }

        if (Path.IsPathRooted(executableName))
        {
            return File.Exists(executableName) ? executableName : null;
        }

        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        var extensions = BuildExtensionCandidates(executableName, pathExt, isWindows);

        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                string candidate;
                try
                {
                    candidate = Path.Combine(directory.Trim(), executableName + extension);
                }
                catch (ArgumentException)
                {
                    // PATH 里混进了非法路径字符的条目，跳过而不是让整次查找失败。
                    break;
                }

                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        return null;
    }

    private static List<string> BuildExtensionCandidates(string executableName, string? pathExt, bool isWindows)
    {
        if (!isWindows)
        {
            return [string.Empty];
        }

        var executableExtensions = (string.IsNullOrWhiteSpace(pathExt) ? DefaultPathExt : pathExt)
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim())
            .Where(e => e.StartsWith('.'))
            .ToList();

        var ownExtension = Path.GetExtension(executableName);
        var alreadyExecutable = !string.IsNullOrEmpty(ownExtension)
                                && executableExtensions.Contains(ownExtension, StringComparer.OrdinalIgnoreCase);

        return alreadyExecutable ? [string.Empty, .. executableExtensions] : executableExtensions;
    }
}
