namespace Tnzi.Documents.Services.Internal;

/// <summary>
/// 定位本机的 Chromium 系浏览器（Chrome / Edge / Chromium）可执行文件。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="LibreOfficeLocator"/> 同一口径：配置了路径就只认配置（配错要立刻报错，
/// 不能悄悄换一个浏览器跑）；没配置才按各操作系统的常见安装路径 + <c>PATH</c> 探测。
/// 探测结果按「配置值」缓存 —— 调用方会在列表页上逐行询问「能不能预览」。
/// </para>
/// <para>
/// ★ 探测顺序把 <b>Edge 排在 Chrome 之后但在 Chromium 之前</b>，而 Windows 段一定要包含 Edge：
/// Windows Server 自带 Edge、通常不装 Chrome，这正是「发布到 IIS 的应用不需要额外搬运浏览器」
/// 这条部署结论的依据。两者同源（都是 Chromium），出的 PDF 没有实质差别。
/// </para>
/// </remarks>
internal static class ChromiumLocator
{
    private static readonly object Sync = new();
    private static string? _cachedFor;
    private static string? _cachedResult;
    private static bool _hasCache;

    /// <summary>解析浏览器可执行文件路径；找不到返回 null。</summary>
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

    /// <summary>组装「找不到浏览器」的可读错误消息（含探测过的路径与配置键）。</summary>
    /// <param name="configuredPath">配置的路径（文件或目录），可为空。</param>
    public static string NotFoundMessage(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return $"No Chromium-based browser was found at the configured path '{configuredPath}'. " +
                   "Fix 'Documents:Html:BrowserPath' (it takes the browser executable or the folder containing it), " +
                   "or clear it to auto-detect.";
        }

        var probed = string.Join(", ", CandidatePaths().Take(6));
        return "HTML is rendered by a headless Chromium-based browser, and none was found. " +
               "Install Google Chrome, Microsoft Edge or Chromium, or set 'Documents:Html:BrowserPath' to its executable. " +
               "Set 'Documents:Html:Enabled' to false to fall back to LibreOffice, which drops most CSS. " +
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
                foreach (var fileName in ExecutableFileNames())
                {
                    var inDirectory = Path.Combine(configuredPath, fileName);
                    if (File.Exists(inDirectory))
                        return inDirectory;
                }
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

    private static IEnumerable<string> ExecutableFileNames()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            yield return "chrome.exe";
            yield return "msedge.exe";
            yield return "chromium.exe";
        }
        else
        {
            yield return "google-chrome";
            yield return "google-chrome-stable";
            yield return "chromium";
            yield return "chromium-browser";
            yield return "microsoft-edge";
            yield return "microsoft-edge-stable";
        }
    }

    private static IEnumerable<string> CandidatePaths()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var roots = new[] { "ProgramFiles", "ProgramW6432", "ProgramFiles(x86)", "LOCALAPPDATA" }
                .Select(Environment.GetEnvironmentVariable)
                .Where(root => !string.IsNullOrEmpty(root))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var root in roots)
                yield return Path.Combine(root!, "Google", "Chrome", "Application", "chrome.exe");

            foreach (var root in roots)
                yield return Path.Combine(root!, "Microsoft", "Edge", "Application", "msedge.exe");

            foreach (var root in roots)
                yield return Path.Combine(root!, "Chromium", "Application", "chrome.exe");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";
            yield return "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge";
            yield return "/Applications/Chromium.app/Contents/MacOS/Chromium";
        }
        else
        {
            yield return "/usr/bin/google-chrome";
            yield return "/usr/bin/google-chrome-stable";
            yield return "/opt/google/chrome/chrome";
            yield return "/usr/bin/microsoft-edge";
            yield return "/usr/bin/chromium";
            yield return "/usr/bin/chromium-browser";
            yield return "/snap/bin/chromium";
        }
    }

    private static string? ProbePath()
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVariable))
            return null;

        var directories = pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var fileName in ExecutableFileNames())
        {
            foreach (var directory in directories)
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
        }

        return null;
    }
}
