using System.Collections.Concurrent;

namespace Tnzi.Localization.Json;

/// <summary>
/// JSON 字符串本地化器工厂
/// 缓存已创建的 localizer 实例，支持按类型和按名称创建
/// </summary>
public class JsonStringLocalizerFactory : IStringLocalizerFactory
{
    private readonly string _resourcesPath;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IMissingTranslationTracker? _missingTranslationTracker;
    private readonly ConcurrentDictionary<string, JsonStringLocalizer> _localizerCache = new();

    public JsonStringLocalizerFactory(IOptions<Microsoft.Extensions.Localization.LocalizationOptions> options, ILoggerFactory loggerFactory, IMissingTranslationTracker? missingTranslationTracker = null)
    {
        Check.NotNull(options);
        _loggerFactory = Check.NotNull(loggerFactory);
        _missingTranslationTracker = missingTranslationTracker;
        _resourcesPath = options.Value.ResourcesPath ?? "Resources";
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
            name => new JsonStringLocalizer(name, _resourcesPath, _loggerFactory, _missingTranslationTracker));
    }
}
