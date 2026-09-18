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
    /// 解析成本率：精确匹配 provider/model → 通配 provider/"*" → 默认
    /// </summary>
    private static ModelCostRate? ResolveCostRate(CostTrackingOptions options, string provider, string model)
    {
        var models = Lookup(options.ModelCosts, provider);
        if (models == null)
            return options.DefaultCostRate;

        // 1. 精确匹配 → 2. 通配匹配 → 3. 默认
        return Lookup(models, model) ?? Lookup(models, "*") ?? options.DefaultCostRate;
    }

    /// <summary>
    /// 大小写不敏感查找。配置绑定器为内层字典 <c>new</c> 出来的实例不带比较器，
    /// 而 provider / model 名在用量日志里的大小写来自各 provider 的回包，不能指望与配置一致。
    /// </summary>
    private static TValue? Lookup<TValue>(Dictionary<string, TValue> dictionary, string key) where TValue : class
    {
        if (dictionary.TryGetValue(key, out var value))
            return value;

        foreach (var (candidate, candidateValue) in dictionary)
        {
            if (string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase))
                return candidateValue;
        }

        return null;
    }
}
