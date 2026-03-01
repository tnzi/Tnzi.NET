namespace Tnzi.Localization.Json;

/// <summary>
/// JSON-based string localizer
/// Loads translation resources from JSON files, supports both flat and nested JSON formats
/// Nested keys are flattened using dot notation (e.g., {"Auth": {"Login": "Login"}} -> key "Auth.Login")
/// Lookup priority: {resourcesPath}/{baseName}.{culture}.json -> {resourcesPath}/{culture}.json
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
    /// Read and parse a JSON file into a flat key-value dictionary.
    /// Supports both flat and nested JSON structures.
    /// Nested objects are flattened using dot notation (e.g., {"Auth": {"Login": "Sign In"}} -> "Auth.Login" = "Sign In").
    /// </summary>
    private Dictionary<string, string> ReadJsonFile(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            using var document = JsonDocument.Parse(json);
            var result = new Dictionary<string, string>();
            FlattenJsonElement(document.RootElement, string.Empty, result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load JSON resource file: {FilePath}", filePath);
            return new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// Recursively flatten a JsonElement into dot-separated key-value pairs.
    /// Object properties are joined with "." separator.
    /// Array elements, null values, and non-string/non-object values are converted to their JSON string representation.
    /// </summary>
    public static void FlattenJsonElement(JsonElement element, string prefix, Dictionary<string, string> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var key = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";
                    FlattenJsonElement(property.Value, key, result);
                }
                break;

            case JsonValueKind.String:
                if (!string.IsNullOrEmpty(prefix))
                {
                    result[prefix] = element.GetString() ?? string.Empty;
                }
                break;

            case JsonValueKind.Number:
                if (!string.IsNullOrEmpty(prefix))
                {
                    result[prefix] = element.GetRawText();
                }
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                if (!string.IsNullOrEmpty(prefix))
                {
                    result[prefix] = element.GetBoolean().ToString().ToLowerInvariant();
                }
                break;

            case JsonValueKind.Array:
                if (!string.IsNullOrEmpty(prefix))
                {
                    // Flatten array elements with numeric index
                    var index = 0;
                    foreach (var item in element.EnumerateArray())
                    {
                        FlattenJsonElement(item, $"{prefix}.{index}", result);
                        index++;
                    }
                }
                break;

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                // Skip null/undefined values
                break;
        }
    }
}
