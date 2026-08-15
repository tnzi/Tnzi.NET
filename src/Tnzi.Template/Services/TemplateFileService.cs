namespace Tnzi.Template.Services;

/// <summary>
/// <see cref="ITemplateFileService"/> 的默认实现：模板文件读取的唯一入口。
/// </summary>
/// <remarks>
/// 「哪些文件算模板、模块和分类怎么从路径推出来、读取到哪里为止」这三件事只在这里写一遍。
/// 同一模块此前已经因为 front matter 的识别规则被写了两份而出过问题
/// （见 <c>FrontMatterExtractor</c> 的说明），文件枚举规则同理不再分散到各调用点。
/// </remarks>
public class TemplateFileService : ITemplateFileService
{
    private readonly TemplateFileParser _parser;
    private readonly TemplateOptions _options;
    private readonly IServiceProvider? _serviceProvider;
    private readonly ILogger<TemplateFileService>? _logger;

    public TemplateFileService(
        TemplateFileParser parser,
        IOptions<TemplateOptions> options,
        IServiceProvider? serviceProvider = null,
        ILogger<TemplateFileService>? logger = null)
    {
        _parser = Check.NotNull(parser);
        _options = Check.NotNull(options).Value;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public bool IsEnabled => _options.EnableFileSystemTemplates;

    private string Extension => string.IsNullOrWhiteSpace(_options.TemplateExtension) ? ".cshtml" : _options.TemplateExtension;

    public Task<TemplateInfo?> FindTemplateAsync(string templateName, string module, string? category = null, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(templateName);
        Check.NotNullOrWhiteSpace(module);

        if (!IsEnabled)
            return Task.FromResult<TemplateInfo?>(null);

        var fileName = templateName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)
            ? templateName
            : templateName + Extension;

        var relativePath = string.IsNullOrWhiteSpace(category)
            ? Path.Combine(module, fileName)
            : Path.Combine(module, category, fileName);

        return ReadTemplateAsync(relativePath, cancellationToken);
    }

    public async Task<TemplateInfo?> ReadTemplateAsync(string path, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(path);

        if (!IsEnabled)
            return null;

        var templateRoots = BuildTemplateRoots();
        var fullPath = ResolveWithinTemplateRoots(path, templateRoots);
        if (fullPath == null)
            return null;

        return await ParseAsync(fullPath, templateRoots, cancellationToken);
    }

    public async Task<IReadOnlyList<TemplateInfo>> ListTemplatesAsync(string? module = null, string? category = null, CancellationToken cancellationToken = default)
    {
        var results = new List<TemplateInfo>();
        if (!IsEnabled)
            return results;

        var templateRoots = BuildTemplateRoots();
        // 同名模板可能在多个搜索根下各有一份（应用目录覆盖框架默认模板）：
        // 与查找时一致，优先级高的根先命中，后面的同键条目丢弃。
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in templateRoots)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (!Directory.Exists(root))
                continue;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*" + Extension, SearchOption.AllDirectories);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger?.LogWarning(ex, "Could not enumerate template files under {Root}", root);
                continue;
            }

            foreach (var file in files)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                if (!TryDeriveIdentity(root, file, out var fileModule, out var fileCategory, out var fileName))
                    continue;

                if (!string.IsNullOrWhiteSpace(module) && !string.Equals(fileModule, module, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrWhiteSpace(category) && !string.Equals(fileCategory, category, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!seen.Add($"{fileModule}::{fileCategory}::{fileName}"))
                    continue;

                // 单个文件读坏不能让整次枚举少一行：仍然给出路径身份，正文留空。
                var info = await ParseAsync(file, templateRoots, cancellationToken)
                    ?? new TemplateInfo { Name = fileName, FilePath = file, LastModified = SafeLastWriteTimeUtc(file) };

                info.Module = fileModule;
                info.Category = fileCategory;
                results.Add(info);
            }
        }

        return results;
    }

    /// <summary>
    /// 模板根列表：搜索根逐个拼上 <c>TemplateRootPath</c>。
    /// <c>TemplateRootPath</c> 本身是绝对路径时它就是唯一的根。
    /// </summary>
    private List<string> BuildTemplateRoots()
    {
        var templateRoot = string.IsNullOrWhiteSpace(_options.TemplateRootPath) ? "Templates" : _options.TemplateRootPath;

        if (Path.IsPathRooted(templateRoot))
        {
            return new List<string> { Path.GetFullPath(templateRoot) };
        }

        var roots = new List<string>();
        foreach (var searchRoot in BuildSearchRoots(_options, _serviceProvider))
        {
            if (string.IsNullOrWhiteSpace(searchRoot))
                continue;

            string combined;
            try
            {
                combined = Path.GetFullPath(Path.Combine(searchRoot, templateRoot));
            }
            catch (Exception ex) when (ex is ArgumentException or PathTooLongException)
            {
                _logger?.LogDebug(ex, "Skipping invalid template search root: {Root}", searchRoot);
                continue;
            }

            if (!roots.Contains(combined, StringComparer.OrdinalIgnoreCase))
                roots.Add(combined);
        }

        return roots;
    }

    /// <summary>
    /// 把调用方给的路径解析成模板根内的绝对路径。
    /// 相对路径按根的优先级依次尝试；绝对路径必须落在某个根内，否则拒绝读取 ——
    /// 模板名/模块名可能来自消费方数据，放行越界路径等于让任意 <c>.cshtml</c> 被当模板读走。
    /// </summary>
    private string? ResolveWithinTemplateRoots(string path, IReadOnlyList<string> templateRoots)
    {
        if (!Path.IsPathRooted(path))
        {
            return FindFileInSearchRoots(path, templateRoots);
        }

        string normalized;
        try
        {
            normalized = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException)
        {
            _logger?.LogDebug(ex, "Invalid template path: {Path}", path);
            return null;
        }

        if (!templateRoots.Any(root => IsWithinRoot(root, normalized)))
        {
            _logger?.LogWarning("Refused to read template file outside the configured template roots: {Path}", path);
            return null;
        }

        return File.Exists(normalized) ? normalized : null;
    }

    private async Task<TemplateInfo?> ParseAsync(string fullPath, IReadOnlyList<string> templateRoots, CancellationToken cancellationToken)
    {
        try
        {
            var info = await _parser.ParseTemplateFileAsync(fullPath, cancellationToken);
            if (info == null)
                return null;

            var root = templateRoots.FirstOrDefault(r => IsWithinRoot(r, fullPath));
            if (root != null && TryDeriveIdentity(root, fullPath, out var module, out var category, out _))
            {
                // 解析器只看得到文件本身，分类取的是上一级目录名；模块与多级分类要相对模板根才推得出来。
                info.Module = module;
                info.Category = category;
            }

            return info;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Could not read template file: {Path}", fullPath);
            return null;
        }
    }

    /// <summary>
    /// 从模板根相对路径推导 模块 / 分类 / 名称：
    /// <c>{module}/{category…}/{name}{扩展名}</c>，分类可以有多级（以 <c>/</c> 连接），也可以没有。
    /// 直接躺在模板根下、没有模块段的文件不算模板。
    /// </summary>
    private static bool TryDeriveIdentity(string root, string fullPath, out string module, out string category, out string name)
    {
        module = string.Empty;
        category = string.Empty;
        name = string.Empty;

        string relative;
        try
        {
            relative = Path.GetRelativePath(root, fullPath);
        }
        catch (ArgumentException)
        {
            return false;
        }

        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parts.Length < 2)
            return false;

        module = parts[0];
        category = parts.Length >= 3 ? string.Join('/', parts.Skip(1).Take(parts.Length - 2)) : string.Empty;
        name = Path.GetFileNameWithoutExtension(parts[^1]);
        return true;
    }

    private DateTime? SafeLastWriteTimeUtc(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogDebug(ex, "Could not stat template file: {Path}", path);
            return null;
        }
    }
}
