namespace Tnzi.AI.Cli.Services;

/// <summary>
/// 在宿主上定位一个 provider 的可执行文件。
/// </summary>
public interface ICliExecutableResolver
{
    /// <summary>
    /// 解析绝对路径。找不到返回 null。
    /// </summary>
    /// <remarks>
    /// 顺序：配置里的显式路径 → PATH 查找默认名（Windows 上按 PATHEXT 逐个后缀试）。
    /// </remarks>
    string? Resolve(CliProviderDescriptor provider);

    /// <summary>
    /// 探测 CLI 版本。<b>仅供观测</b>，绝不用于选择行为分支。
    /// </summary>
    Task<string?> DetectVersionAsync(string executablePath, CancellationToken cancellationToken);
}

/// <inheritdoc cref="ICliExecutableResolver" />
public class CliExecutableResolver : ICliExecutableResolver
{
    private static readonly TimeSpan VersionProbeTimeout = TimeSpan.FromSeconds(10);

    private readonly ILogger<CliExecutableResolver> _logger;

    /// <summary>初始化可执行文件解析器。</summary>
    public CliExecutableResolver(ILogger<CliExecutableResolver> logger)
    {
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public string? Resolve(CliProviderDescriptor provider)
    {
        Check.NotNull(provider);

        if (!string.IsNullOrWhiteSpace(provider.ExecutablePathOverride))
        {
            return File.Exists(provider.ExecutablePathOverride) ? provider.ExecutablePathOverride : null;
        }

        return SearchPath(provider.DefaultExecutable);
    }

    /// <inheritdoc />
    public async Task<string?> DetectVersionAsync(string executablePath, CancellationToken cancellationToken)
    {
        Check.NotNullOrWhiteSpace(executablePath);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("--version");

        try
        {
            if (!process.Start())
            {
                return null;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(VersionProbeTimeout);

            var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);

            var version = output.Trim();
            return string.IsNullOrEmpty(version) ? null : version.Split('\n')[0].Trim();
        }
        catch (OperationCanceledException)
        {
            // 探测卡住的 CLI 不该拖住启动流程（版本只是展示信息），但也不能把它留在那里 ——
            // 每次探测周期泄漏一个进程，几天后宿主上就是一片僵尸。
            _logger.LogDebug("Version probe for {Executable} timed out; killing the probe process", executablePath);
            TryKillProbe(process);
            return null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or SystemException)
        {
            _logger.LogDebug(ex, "Could not detect version for {Executable}", executablePath);
            return null;
        }
    }

    /// <summary>
    /// 在 PATH 上查找可执行文件。
    /// </summary>
    /// <remarks>
    /// Windows 上必须按 <c>PATHEXT</c> 逐个后缀试：很多 CLI 是通过 npm 安装的，
    /// 落在 PATH 里的是 <c>claude.cmd</c> 而不是 <c>claude</c>，只找裸名会一无所获。
    /// </remarks>
    private static string? SearchPath(string executableName)
    {
        if (string.IsNullOrWhiteSpace(executableName))
        {
            return null;
        }

        if (Path.IsPathRooted(executableName))
        {
            return File.Exists(executableName) ? executableName : null;
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        var extensions = BuildExtensionCandidates();

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

    private static List<string> BuildExtensionCandidates()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [string.Empty];
        }

        var pathExt = Environment.GetEnvironmentVariable("PATHEXT")
                      ?? ".COM;.EXE;.BAT;.CMD";

        var extensions = new List<string> { string.Empty };
        extensions.AddRange(pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim())
            .Where(e => e.StartsWith('.')));

        return extensions;
    }

    private void TryKillProbe(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or SystemException)
        {
            _logger.LogDebug(ex, "Could not kill the timed-out version probe process");
        }
    }
}
