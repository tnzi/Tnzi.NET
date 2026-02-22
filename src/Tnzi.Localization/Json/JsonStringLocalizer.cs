using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace Tnzi.Localization.Json;

/// <summary>
/// 基于 JSON 文件的字符串本地化器
/// 从 JSON 文件加载翻译资源，支持扁平键值对格式
/// 查找路径优先级：{resourcesPath}/{baseName}.{culture}.json -> {resourcesPath}/{culture}.json
/// </summary>
public class JsonStringLocalizer : IStringLocalizer
{
    private readonly string _baseName;
    private readonly string _resourcesPath;
    private readonly ILogger _logger;
    private readonly IMissingTranslationTracker? _missingTranslationTracker;
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _resourceCache = new();

    public JsonStringLocalizer(string baseName, string resourcesPath, ILoggerFactory loggerFactory, IMissingTranslationTracker? missingTranslationTracker = null)
    {
        _baseName = Check.NotNull(baseName);
        _resourcesPath = Check.NotNull(resourcesPath);
        _logger = Check.NotNull(loggerFactory).CreateLogger<JsonStringLocalizer>();
        _missingTranslationTracker = missingTranslationTracker;
    }

    /// <summary>
    /// 根据 key 获取本地化字符串
    /// </summary>
    public LocalizedString this[string name]
    {
        get
        {
            Check.NotNull(name);
            var value = GetStringSafely(name);
            return new LocalizedString(name, value ?? name, resourceNotFound: value == null);
        }
    }

    /// <summary>
    /// 根据 key 和参数获取格式化的本地化字符串
    /// </summary>
    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            Check.NotNull(name);
            var format = GetStringSafely(name);
            var value = format != null ? string.Format(CultureInfo.CurrentCulture, format, arguments) : name;
            return new LocalizedString(name, value, resourceNotFound: format == null);
        }
    }

    /// <summary>
    /// 获取所有本地化字符串
    /// </summary>
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        var culture = CultureInfo.CurrentUICulture;
        var resources = LoadJsonResource(culture);

        foreach (var kvp in resources)
        {
            yield return new LocalizedString(kvp.Key, kvp.Value, resourceNotFound: false);
        }

        // 包含父文化的翻译
        if (includeParentCultures && culture.Parent != CultureInfo.InvariantCulture)
        {
            var parentResources = LoadJsonResource(culture.Parent);
            foreach (var kvp in parentResources)
            {
                // 不覆盖子文化已有的翻译
                if (!resources.ContainsKey(kvp.Key))
                {
                    yield return new LocalizedString(kvp.Key, kvp.Value, resourceNotFound: false);
                }
            }
        }
    }

    /// <summary>
    /// 安全地获取翻译字符串，找不到时追踪缺失并返回 null
    /// </summary>
    private string? GetStringSafely(string name)
    {
        var culture = CultureInfo.CurrentUICulture;
        var resources = LoadJsonResource(culture);

        if (resources.TryGetValue(name, out var value))
        {
            return value;
        }

        // 尝试从父文化加载
        if (culture.Parent != CultureInfo.InvariantCulture)
        {
            var parentResources = LoadJsonResource(culture.Parent);
            if (parentResources.TryGetValue(name, out value))
            {
                return value;
            }
        }

        // 追踪缺失的翻译
        _missingTranslationTracker?.TrackMissing(culture.Name, name);

        return null;
    }

    /// <summary>
    /// 加载指定文化的 JSON 资源文件
    /// 使用 ConcurrentDictionary 缓存避免重复读取
    /// </summary>
    private Dictionary<string, string> LoadJsonResource(CultureInfo culture)
    {
        var cacheKey = $"{_baseName}.{culture.Name}";
        return _resourceCache.GetOrAdd(cacheKey, _ => LoadJsonResourceFromFile(culture));
    }

    /// <summary>
    /// 从文件系统读取 JSON 资源
    /// 查找路径优先级：{resourcesPath}/{baseName}.{culture}.json -> {resourcesPath}/{culture}.json
    /// </summary>
    private Dictionary<string, string> LoadJsonResourceFromFile(CultureInfo culture)
    {
        // 优先查找带 baseName 的资源文件
        var specificPath = Path.Combine(_resourcesPath, $"{_baseName}.{culture.Name}.json");
        if (File.Exists(specificPath))
        {
            return ReadJsonFile(specificPath);
        }

        // 回退到通用文化资源文件
        var generalPath = Path.Combine(_resourcesPath, $"{culture.Name}.json");
        if (File.Exists(generalPath))
        {
            return ReadJsonFile(generalPath);
        }

        return new Dictionary<string, string>();
    }

    /// <summary>
    /// 读取并解析 JSON 文件为键值对字典
    /// </summary>
    private Dictionary<string, string> ReadJsonFile(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            var result = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return result ?? new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load JSON resource file: {FilePath}", filePath);
            return new Dictionary<string, string>();
        }
    }
}
