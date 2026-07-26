namespace Tnzi.Template.Internal;

/// <summary>
/// 模板路径辅助类
/// 提供模板文件路径解析和搜索路径构建的公共功能
/// </summary>
internal static class TemplatePathHelper
{
    /// <summary>
    /// 获取应用程序基础路径（输出目录）
    /// </summary>
    public static string GetApplicationBasePath()
    {
        // 优先使用 AppContext.BaseDirectory，这是模板文件复制到的实际位置
        return AppContext.BaseDirectory;
    }

    /// <summary>
    /// 获取 ContentRootPath（项目根目录，用于开发环境下的源文件访问）
    /// </summary>
    public static string? GetContentRootPath(IServiceProvider? serviceProvider)
    {
        var webEnvironment = serviceProvider?.GetService<IWebHostEnvironment>();
        var hostEnvironment = serviceProvider?.GetService<IHostEnvironment>();
        return webEnvironment?.ContentRootPath ?? hostEnvironment?.ContentRootPath;
    }

    /// <summary>
    /// 构建模板搜索根路径列表（按优先级排序）
    /// </summary>
    public static List<string> BuildSearchRoots(TemplateOptions? options, IServiceProvider? serviceProvider = null)
    {
        var searchRoots = new List<string>();

        // 1. 优先级最高：AppContext.BaseDirectory（输出目录，如 bin/Debug/net10.0）
        //    这是模板文件通过 CopyToOutputDirectory 复制到的位置
        var appBaseDir = GetApplicationBasePath();
        searchRoots.Add(appBaseDir);

        // 2. 额外搜索路径（由 Host 模块或其他模块配置）
        if (options?.AdditionalSearchPaths != null)
        {
            foreach (var additionalPath in options.AdditionalSearchPaths)
            {
                if (string.IsNullOrWhiteSpace(additionalPath))
                    continue;

                // 如果是绝对路径，直接添加；否则基于 AppBaseDir 解析
                var resolvedPath = Path.IsPathRooted(additionalPath)
                    ? additionalPath
                    : Path.GetFullPath(Path.Combine(appBaseDir, additionalPath));

                if (!searchRoots.Contains(resolvedPath, StringComparer.OrdinalIgnoreCase))
                {
                    searchRoots.Add(resolvedPath);
                }
            }
        }

        // 3. ContentRootPath（项目根目录，在发布环境中可能与 AppBaseDir 相同）
        var contentRoot = GetContentRootPath(serviceProvider);
        if (!string.IsNullOrWhiteSpace(contentRoot) &&
            !searchRoots.Contains(contentRoot, StringComparer.OrdinalIgnoreCase))
        {
            searchRoots.Add(contentRoot);
        }

        return searchRoots;
    }

    /// <summary>
    /// 在搜索路径中查找文件。
    /// 相对路径由模板名/模块名/分类名拼出，这些值可能来自消费方数据（如发票的 TemplateName），
    /// 因此解析后必须仍落在搜索根内：越界候选一律跳过，避免把根目录外的 .cshtml
    /// 当模板加载并编译执行。
    /// </summary>
    /// <param name="relativePath">相对路径</param>
    /// <param name="searchRoots">搜索根路径列表</param>
    /// <returns>找到的文件完整路径，如果未找到则返回 null</returns>
    public static string? FindFileInSearchRoots(string relativePath, IEnumerable<string> searchRoots)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        foreach (var root in searchRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            if (!TryResolveWithinRoot(root, relativePath, out var fullPath))
                continue;

            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return null;
    }

    /// <summary>
    /// 把相对路径解析为搜索根内的绝对路径；越界或路径非法时返回 false。
    /// 前缀比较带上目录分隔符，否则同级的兄弟目录（root 为 "…/app" 时的 "…/app_bak"）会被误判为在根内。
    /// </summary>
    private static bool TryResolveWithinRoot(string root, string relativePath, out string fullPath)
    {
        fullPath = string.Empty;

        try
        {
            var normalizedRoot = Path.GetFullPath(root);
            var rootPrefix = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
                ? normalizedRoot
                : normalizedRoot + Path.DirectorySeparatorChar;

            var resolved = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
            if (!resolved.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                return false;

            fullPath = resolved;
            return true;
        }
        catch (ArgumentException)
        {
            // 路径含非法字符：视为未找到
            return false;
        }
        catch (PathTooLongException)
        {
            return false;
        }
    }
}
