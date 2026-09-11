namespace Tnzi.Localization.Json;

/// <summary>
/// JSON-based string localizer
/// Loads translation resources from JSON files, supports both flat and nested JSON formats
/// Nested keys are flattened using dot notation (e.g., {"Auth": {"Login": "Login"}} -> key "Auth.Login")
/// File lookup: {resourcesPath}/{baseName}.{culture}.json -> {resourcesPath}/{culture}.json
///
/// 键的查找链是三级：当前文化 → 父文化 → <c>Localization:DefaultCulture</c>。
///
/// ★ 第三级不是可有可无的：没有它时，一个只写在默认语言资源里的键，在别的语言下
/// 返回的是**键名本身** —— 界面上直接显示 <c>Auth.Login.Title</c>。Resx 模式有中性资源
/// 兜底，所以同一套配置换个 <c>ResourceFormat</c> 会突然变弱，而这一点在配置上看不出来。
/// </summary>
public class JsonStringLocalizer : IStringLocalizer
{
    private readonly string _baseName;
    private readonly string _resourcesPath;
    private readonly ILogger _logger;
    private readonly IMissingTranslationTracker? _missingTranslationTracker;
    private readonly CultureInfo? _defaultCulture;
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _resourceCache = new();

    /// <summary>
    /// 初始化一个 <see cref="JsonStringLocalizer"/> 实例
    /// </summary>
    /// <param name="baseName">资源基名</param>
    /// <param name="resourcesPath">资源目录</param>
    /// <param name="loggerFactory">日志工厂</param>
    /// <param name="missingTranslationTracker">缺失翻译追踪器（可选）</param>
    /// <param name="defaultCulture">
    /// 查找链最后一级的默认语言（<c>Localization:DefaultCulture</c>）。
    /// 传 null 时退回两级查找。
    /// </param>
    public JsonStringLocalizer(
        string baseName,
        string resourcesPath,
        ILoggerFactory loggerFactory,
        IMissingTranslationTracker? missingTranslationTracker = null,
        string? defaultCulture = null)
    {
        _baseName = Check.NotNull(baseName);
        _resourcesPath = Check.NotNull(resourcesPath);
        _logger = Check.NotNull(loggerFactory).CreateLogger<JsonStringLocalizer>();
        _missingTranslationTracker = missingTranslationTracker;
        _defaultCulture = ParseDefaultCulture(defaultCulture);
    }

    /// <summary>
    /// 解析默认语言名。配错时只记一条警告然后退回两级查找 —— 一个拼错的语言名
    /// 不该让整个应用启动失败，但也不能一声不吭。
    /// </summary>
    private CultureInfo? ParseDefaultCulture(string? defaultCulture)
    {
        if (string.IsNullOrWhiteSpace(defaultCulture)) return null;

        try
        {
            return new CultureInfo(defaultCulture);
        }
        catch (CultureNotFoundException)
        {
            _logger.LogWarning(
                "Localization:DefaultCulture '{DefaultCulture}' is not a valid culture name; "
                + "JSON lookups will not fall back to it.",
                defaultCulture);
            return null;
        }
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
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var source in BuildLookupChain(culture, includeParentCultures))
        {
            foreach (var kvp in LoadJsonResource(source))
            {
                // 更靠前的文化已经给出的键不被后面的覆盖
                if (seen.Add(kvp.Key))
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

        foreach (var source in BuildLookupChain(culture, includeFallbacks: true))
        {
            if (LoadJsonResource(source).TryGetValue(name, out var value))
            {
                return value;
            }
        }

        // 整条链都没有才算缺失 —— 被默认语言兜住的键不该刷满缺失报告
        _missingTranslationTracker?.TrackMissing(culture.Name, name);

        return null;
    }

    /// <summary>
    /// 查找顺序：当前文化及其**整条**父链 → 默认语言及其整条父链。
    /// 同名文化只出现一次（请求的就是默认语言时不重复枚举）。
    ///
    /// ★ 必须走完整条父链而不是只上一级：带脚本子标签的文化中间还夹着一层，
    /// <c>zh-CN</c> 的父是 <c>zh-Hans</c> 而不是 <c>zh</c>（ICU 的层级）。只上一级的话，
    /// 一个按 <c>zh.json</c> 组织资源的应用在 <c>zh-CN</c> 请求下一条都命中不了，
    /// 而在 <c>fr-FR</c>（父就是 <c>fr</c>）下工作正常 —— 于是这个缺陷只在部分语言上出现。
    /// </summary>
    /// <param name="culture">请求的文化</param>
    /// <param name="includeFallbacks">是否包含父链与默认语言</param>
    private IEnumerable<CultureInfo> BuildLookupChain(CultureInfo culture, bool includeFallbacks)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in Ancestry(culture))
        {
            if (seen.Add(source.Name)) yield return source;
            if (!includeFallbacks) yield break;
        }

        if (!includeFallbacks || _defaultCulture == null) yield break;

        foreach (var source in Ancestry(_defaultCulture))
        {
            if (seen.Add(source.Name)) yield return source;
        }
    }

    /// <summary>
    /// 一个文化自己加上它的每一级父文化，到不变文化为止（不含不变文化）。
    /// </summary>
    private static IEnumerable<CultureInfo> Ancestry(CultureInfo culture)
    {
        for (var current = culture;
             current != null && !string.IsNullOrEmpty(current.Name);
             current = current.Parent)
        {
            yield return current;

            // CultureInfo.Parent 在不变文化上返回自身，不加这一步会死循环
            if (current.Parent.Equals(current)) yield break;
        }
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
