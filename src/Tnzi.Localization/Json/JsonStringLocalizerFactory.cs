namespace Tnzi.Localization.Json;

/// <summary>
/// JSON 字符串本地化器工厂
/// 缓存已创建的 localizer 实例，支持按类型和按名称创建
/// </summary>
public class JsonStringLocalizerFactory : IStringLocalizerFactory
{
    private readonly string _resourcesPath;
    private readonly string? _defaultCulture;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IMissingTranslationTracker? _missingTranslationTracker;
    private readonly ConcurrentDictionary<string, JsonStringLocalizer> _localizerCache = new();

    /// <summary>
    /// 初始化一个 <see cref="JsonStringLocalizerFactory"/> 实例
    /// </summary>
    /// <param name="options">ASP.NET Core 的本地化选项（提供资源目录）</param>
    /// <param name="loggerFactory">日志工厂</param>
    /// <param name="missingTranslationTracker">缺失翻译追踪器（可选）</param>
    /// <param name="tnziOptions">
    /// 框架的本地化选项。<c>DefaultCulture</c> 是查找链的最后一级 —— 没有它时，
    /// 只写在默认语言资源里的键在别的语言下会退化成键名本身。
    /// </param>
    public JsonStringLocalizerFactory(
        IOptions<Microsoft.Extensions.Localization.LocalizationOptions> options,
        ILoggerFactory loggerFactory,
        IMissingTranslationTracker? missingTranslationTracker = null,
        IOptions<Tnzi.Localization.Options.LocalizationOptions>? tnziOptions = null)
    {
        Check.NotNull(options);
        _loggerFactory = Check.NotNull(loggerFactory);
        _missingTranslationTracker = missingTranslationTracker;
        _resourcesPath = options.Value.ResourcesPath ?? "Resources";
        _defaultCulture = tnziOptions?.Value.DefaultCulture;
    }

    /// <summary>
    /// 根据类型创建本地化器
    /// </summary>
    public IStringLocalizer Create(Type resourceSource)
    {
        Check.NotNull(resourceSource);
        var baseName = resourceSource.Name;
        return CreateLocalizer(baseName);
    }

    /// <summary>
    /// 根据名称和位置创建本地化器
    /// </summary>
    public IStringLocalizer Create(string baseName, string location)
    {
        Check.NotNull(baseName);
        Check.NotNull(location);
        return CreateLocalizer(baseName);
    }

    /// <summary>
    /// 创建或从缓存获取本地化器实例
    /// </summary>
    private JsonStringLocalizer CreateLocalizer(string baseName)
    {
        return _localizerCache.GetOrAdd(baseName,
            name => new JsonStringLocalizer(name, _resourcesPath, _loggerFactory, _missingTranslationTracker, _defaultCulture));
    }
}
