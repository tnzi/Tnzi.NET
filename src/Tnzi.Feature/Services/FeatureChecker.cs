namespace Tnzi.Feature.Services;

/// <summary>
/// Default implementation of IFeatureChecker.
/// Evaluates provider chain in descending priority order,
/// falling back to the feature definition's default value.
/// </summary>
public class FeatureChecker : IFeatureChecker
{
    private readonly IFeatureManager _featureManager;
    private readonly IReadOnlyList<IFeatureValueProvider> _sortedProviders;
    private readonly IFeatureUsageService? _featureUsageService;
    private readonly ILogger<FeatureChecker>? _logger;

    /// <summary>
    /// Initialize FeatureChecker
    /// </summary>
    public FeatureChecker(
        IFeatureManager featureManager,
        IEnumerable<IFeatureValueProvider> providers,
        IFeatureUsageService? featureUsageService = null,
        ILogger<FeatureChecker>? logger = null)
    {
        _featureManager = Check.NotNull(featureManager);
        Check.NotNull(providers);
        // Cache sorted providers to avoid re-sorting on every call
        _sortedProviders = providers.OrderByDescending(p => p.Priority).ToList().AsReadOnly();
        _featureUsageService = featureUsageService;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// ★ Boolean 功能的值解析不出布尔值时按<b>关闭</b>处理并记一条 Warning（失败关闭）。此前任何非空值都算开启：
    /// 默认值或某个作用域的值写成 "0" / "off" / "no" 意图关闭，结果是 <c>[RequireFeature]</c> 守护的端点
    /// 对所有人放行，而管理端显示的生效值还是 "0"。写入面现在会拒绝这类值，这里兜住存量数据与代码里声明的默认值。
    /// 非 Boolean 功能保持「有值即开启」。
    /// </remarks>
    public async Task<bool> IsEnabledAsync(string featureName)
    {
        Check.NotNullOrWhiteSpace(featureName);

        var (definition, value) = await ResolveAsync(featureName);
        bool isEnabled;

        if (definition == null || value == null)
        {
            isEnabled = false;
        }
        else if (bool.TryParse(value, out var boolValue))
        {
            isEnabled = boolValue;
        }
        else if (definition.ValueType == FeatureValueType.Boolean)
        {
            isEnabled = false;
            _logger?.LogWarning(
                "Boolean feature {FeatureName} has the value '{Value}', which is not 'true' or 'false'; treating it as disabled.",
                featureName, value);
        }
        else
        {
            // For non-Boolean features, having any value means "enabled"
            isEnabled = !string.IsNullOrWhiteSpace(value);
        }

        // 用量记录必须在当前调用链内 await：IFeatureUsageService 是 Scoped，用的是本请求的
        // DbContext。丢进 Task.Run 会让后台线程与请求线程并发使用同一个 DbContext
        //（非线程安全，可能连带把请求自身的操作打断），且请求结束后 scope 已释放 ——
        // 记录静默失败。RecordUsageAsync 内部自行吞掉并记录异常，await 它不会影响调用方。
        // 记定义的规范名：同一个功能按调用方的大小写分成几份统计，按名查用量时只看得到其中一份。
        if (_featureUsageService != null)
        {
            await _featureUsageService.RecordUsageAsync(definition?.Name ?? featureName, isEnabled, "FeatureChecker");
        }

        return isEnabled;
    }

    /// <inheritdoc />
    public async Task<T?> GetValueAsync<T>(string featureName) where T : struct
    {
        var value = await GetValueAsync(featureName);
        if (value == null)
            return null;

        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<string?> GetValueAsync(string featureName)
    {
        Check.NotNullOrWhiteSpace(featureName);

        var (_, value) = await ResolveAsync(featureName);
        return value;
    }

    /// <summary>
    /// 取定义与生效值：定义不存在或已停用时两者都为空。
    /// </summary>
    /// <remarks>
    /// ★ 定义按名查找不分大小写，provider 却拿到什么名字就按什么名字查值、按什么名字建缓存键，
    /// 而值的写入、缓存失效事件用的都是定义自己的写法。调用方写 <c>"orders.advancedSearch"</c>、
    /// 定义叫 <c>"Orders.AdvancedSearch"</c> 时，PostgreSQL 上管理员设的值永远查不到（按原样比较），
    /// 其它库上则是缓存条目永远不会被管理端的写入失效。所以这里一律用定义的规范名问 provider。
    /// </remarks>
    private async Task<(FeatureDefinitionRecord? Definition, string? Value)> ResolveAsync(string featureName)
    {
        var definition = await _featureManager.GetOrNullAsync(featureName);
        if (definition == null || !definition.IsEnabled)
            return (null, null);

        // Evaluate providers in descending priority order (pre-sorted)
        foreach (var provider in _sortedProviders)
        {
            var value = await provider.GetOrNullAsync(definition.Name);
            if (value != null)
                return (definition, value);
        }

        // Fall back to default value
        return (definition, definition.DefaultValue);
    }
}
