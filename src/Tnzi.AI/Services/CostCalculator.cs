namespace Tnzi.AI.Services;

/// <summary>
/// 成本计算服务实现 - 基于配置的模型成本率计算 Token 使用的美元成本
/// </summary>
public class CostCalculator : ICostCalculator
{
    private readonly IOptionsMonitor<AIOptions> _options;

    public CostCalculator(IOptionsMonitor<AIOptions> options)
    {
        _options = Check.NotNull(options);
    }

    public decimal? CalculateCost(string provider, string model, int inputTokens, int outputTokens)
    {
        var costOptions = _options.CurrentValue.CostTracking;
        if (!costOptions.Enabled) return null;

        var rate = ResolveCostRate(costOptions, provider, model);
        if (rate == null) return null;

        // 成本 = (inputTokens * inputRate + outputTokens * outputRate) / 1,000,000
        var cost = (inputTokens * rate.InputCostPer1MTokens + outputTokens * rate.OutputCostPer1MTokens) / 1_000_000m;
        return Math.Round(cost, 6); // 保留 6 位小数精度
    }

    /// <summary>
    /// 解析成本率：精确匹配 "provider:model" → 通配 "provider:*" → 默认
    /// </summary>
    private static ModelCostRate? ResolveCostRate(CostTrackingOptions options, string provider, string model)
    {
        // 1. 精确匹配
        var key = $"{provider}:{model}";
        if (options.ModelCosts.TryGetValue(key, out var rate))
            return rate;

        // 2. 通配匹配
        var wildcardKey = $"{provider}:*";
        if (options.ModelCosts.TryGetValue(wildcardKey, out rate))
            return rate;

        // 3. 默认
        return options.DefaultCostRate;
    }
}
