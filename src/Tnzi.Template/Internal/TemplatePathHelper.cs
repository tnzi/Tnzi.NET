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
    /// 构建<b>模板根</b>列表（按优先级排序）：每一项都是一个可以直接在下面找
    /// <c>{module}/{category}/{name}</c> 与 <c>Layouts/{category}/_{name}</c> 的目录。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 顺序：<c>AppContext.BaseDirectory/TemplateRootPath</c>（或绝对的 <c>TemplateRootPath</c> 本身）
    /// → <c>AdditionalSearchPaths</c> 逐项<b>原样</b>（相对项基于 <c>AppContext.BaseDirectory</c> 解析）
    /// → <c>ContentRootPath/TemplateRootPath</c>（开发期源码目录）。
    /// </para>
    /// <para>
    /// ★ <c>AdditionalSearchPaths</c> 的每一项<b>就是一个模板根</b>，与 <c>TemplateRootPath</c> 同级，
    /// 不再拼 <c>TemplateRootPath</c>。此前这里出的是「搜索根」而调用方各自再拼一次 <c>TemplateRootPath</c>：
    /// <c>TemplateOptionsPostConfigure</c> 按文档语义加进来的 <c>&lt;模块目录&gt;/Templates</c> 于是被解析成
    /// <c>&lt;模块目录&gt;/Templates/Templates</c>，一个不存在的目录 —— 程序集扫描每次启动都记一行成功日志却一个
    /// 模板都找不到，照文档配 <c>["D:/shared/Templates"]</c> 的部署得到的只是 404。三条消费路径
    /// （文件模板 / 布局加载 / 布局扫描）此前各拼各的，这一份是唯一出口。
    /// </para>
    /// </remarks>
    public static List<string> BuildTemplateRoots(TemplateOptions? options, IServiceProvider? serviceProvider = null)
    {
        var templateRoot = string.IsNullOrWhiteSpace(options?.TemplateRootPath) ? "Templates" : options.TemplateRootPath;
        var appBaseDir = GetApplicationBasePath();
        var roots = new List<string>();

        // 1. 主模板根：绝对路径原样；相对路径基于输出目录（模板文件经 CopyToOutputDirectory 复制到这里）
        AddRoot(roots, Path.IsPathRooted(templateRoot) ? templateRoot : Path.Combine(appBaseDir, templateRoot));

        // 2. 附加模板根：逐项原样（相对项基于输出目录）
        if (options?.AdditionalSearchPaths != null)
        {
            foreach (var additionalPath in options.AdditionalSearchPaths)
            {
                if (string.IsNullOrWhiteSpace(additionalPath))
                    continue;

                AddRoot(roots, Path.IsPathRooted(additionalPath) ? additionalPath : Path.Combine(appBaseDir, additionalPath));
            }
        }

        // 3. 开发期源码目录下的模板根（发布环境中通常与 #1 相同，去重后不出现）
        var contentRoot = GetContentRootPath(serviceProvider);
        if (!string.IsNullOrWhiteSpace(contentRoot) && !Path.IsPathRooted(templateRoot))
        {
            AddRoot(roots, Path.Combine(contentRoot, templateRoot));
        }

        return roots;
    }

    /// <summary>规范化后去重加入；路径非法时跳过（一个坏配置项不该让整个查找失效）。</summary>
    private static void AddRoot(List<string> roots, string candidate)
    {
        string normalized;
        try
        {
            normalized = Path.GetFullPath(candidate);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return;
        }

        if (!roots.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            roots.Add(normalized);
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
    /// 判断已规范化的绝对路径是否位于某个根目录内。
    /// 与 <see cref="TryResolveWithinRoot"/> 用同一套前缀比较规则（带目录分隔符），
    /// 供调用方拿到一个绝对路径后反向校验其归属。
    /// </summary>
    public static bool IsWithinRoot(string? root, string normalizedFullPath)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(normalizedFullPath))
            return false;

        try
        {
            var normalizedRoot = Path.GetFullPath(root);
            var rootPrefix = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
                ? normalizedRoot
                : normalizedRoot + Path.DirectorySeparatorChar;

            return normalizedFullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (PathTooLongException)
        {
            return false;
        }
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
