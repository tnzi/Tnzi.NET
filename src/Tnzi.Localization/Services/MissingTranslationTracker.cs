using System.Collections.Concurrent;

namespace Tnzi.Localization.Services;

/// <summary>
/// 缺失翻译追踪器实现
/// 在开发环境下输出警告日志，使用 ConcurrentDictionary 避免重复日志
/// </summary>
public class MissingTranslationTracker : IMissingTranslationTracker
{
    private readonly ILogger<MissingTranslationTracker> _logger;
    private readonly bool _isDevelopment;
    private readonly ConcurrentDictionary<string, byte> _reportedKeys = new();

    public MissingTranslationTracker(ILogger<MissingTranslationTracker> logger, IHostEnvironment environment)
    {
        _logger = Check.NotNull(logger);
        _isDevelopment = Check.NotNull(environment).IsDevelopment();
    }

    /// <summary>
    /// 追踪缺失的翻译 key
    /// 仅在开发环境下输出警告日志，且每个 key 只报告一次
    /// </summary>
    public void TrackMissing(string culture, string key)
    {
        if (!_isDevelopment)
        {
            return;
        }

        var cacheKey = $"{culture}:{key}";
        if (_reportedKeys.TryAdd(cacheKey, 0))
        {
            _logger.LogWarning("Missing translation for culture '{Culture}', key '{Key}'", culture, key);
        }
    }
}
