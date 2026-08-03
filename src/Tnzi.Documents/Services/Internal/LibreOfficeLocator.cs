namespace Tnzi.Documents.Services.Internal;

/// <summary>
/// 定位 LibreOffice 的可执行文件（<c>soffice</c>）。
/// </summary>
/// <remarks>
/// 配置了路径就只认配置（配错要立刻报错，不能悄悄换一个版本跑）；没配置才按各操作系统的
/// 常见安装路径 + <c>PATH</c> 探测。探测结果按「配置值」缓存，配置热更新后会重新探测。
/// </remarks>
internal static class LibreOfficeLocator
{
    private const string ExecutableName = "soffice";

    private static readonly object Sync = new();
    private static string? _cachedFor;
    private static string? _cachedResult;
    private static bool _hasCache;

    /// <summary>解析可执行文件路径；找不到返回 null。</summary>
    /// <param name="configuredPath">配置的路径（文件或目录），可为空。</param>
    public static string? Resolve(string? configuredPath)
    {
        var key = configuredPath ?? string.Empty;

        lock (Sync)
        {
            if (_hasCache && string.Equals(_cachedFor, key, StringComparison.Ordinal))
                return _cachedResult;

            _cachedResult = Probe(configuredPath);
            _cachedFor = key;
            _hasCache = true;
            return _cachedResult;
        }
    }

    /// <summary>组装「找不到 LibreOffice」的可读错误消息（含探测过的路径与配置键）。</summary>
    /// <param name="configuredPath">配置的路径（文件或目录），可为空。</param>
    public static string NotFoundMessage(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return $"LibreOffice was not found at the configured path '{configuredPath}'. " +
                   "Fix 'Documents:LibreOfficePath' (it takes the soffice executable or the folder containing it), " +
                   "or clear it to auto-detect.";
        }

        var probed = string.Join(", ", CandidatePaths().Take(8));
        return "LibreOffice was not found. Install it, or set 'Documents:LibreOfficePath' to the soffice executable. " +
               $"Probed: {probed} and every PATH entry.";
    }

    /// <summary>清空缓存（测试用；探测结果依赖机器状态）。</summary>
    public static void ResetCache()
    {
        lock (Sync)
        {
            _hasCache = false;
            _cachedFor = null;
            _cachedResult = null;
        }
    }

    private static string? Probe(string? configuredPath)
    {
        // 配置优先且**不回退**：配了就必须命中，否则报错让人去修配置。
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            if (File.Exists(configuredPath))
                return configuredPath;

            if (Directory.Exists(configuredPath))
            {
                var inDirectory = Path.Combine(configuredPath, ExecutableFileName());
                return File.Exists(inDirectory) ? inDirectory : null;
            }

            return null;
        }

        foreach (var candidate in CandidatePaths())
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return ProbePath();
    }

    private static string ExecutableFileName()
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ExecutableName + ".exe" : ExecutableName;

    private static IEnumerable<string> CandidatePaths()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            foreach (var variable in new[] { "ProgramFiles", "ProgramW6432", "ProgramFiles(x86)", "LOCALAPPDATA" })
            {
                var root = Environment.GetEnvironmentVariable(variable);
                if (!string.IsNullOrEmpty(root))
                    yield return Path.Combine(root, "LibreOffice", "program", "soffice.exe");
            }

            yield return @"C:\Program Files\LibreOffice\program\soffice.exe";
            yield return @"C:\Program Files (x86)\LibreOffice\program\soffice.exe";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return "/Applications/LibreOffice.app/Contents/MacOS/soffice";
            yield return "/opt/homebrew/bin/soffice";
            yield return "/usr/local/bin/soffice";
        }
        else
        {
            yield return "/usr/bin/soffice";
            yield return "/usr/local/bin/soffice";
            yield return "/usr/lib/libreoffice/program/soffice";
            yield return "/usr/lib64/libreoffice/program/soffice";
            yield return "/opt/libreoffice/program/soffice";
            yield return "/snap/bin/libreoffice";
            yield return "/usr/bin/libreoffice";
        }
    }

    private static string? ProbePath()
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVariable))
            return null;

        var fileName = ExecutableFileName();
        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string candidate;
            try
            {
                candidate = Path.Combine(directory, fileName);
            }
            catch (ArgumentException)
            {
                // PATH 里混进了非法路径字符，跳过这一项
                continue;
            }

            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
