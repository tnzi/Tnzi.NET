namespace Tnzi.AI.Options;

/// <summary>
/// USD 成本预算配置选项
/// </summary>
public class BudgetOptions
{
    /// <summary>
    /// 是否启用 USD 预算管控（默认关闭）
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 默认每月预算上限（美元，默认 100）
    /// </summary>
    public decimal DefaultMonthlyBudgetUsd { get; set; } = 100m;

    /// <summary>
    /// 预警阈值（0-1，达到该比例时触发 Warning，默认 0.8）
    /// </summary>
    public double WarningThreshold { get; set; } = 0.8;

    /// <summary>
    /// 按 Agent 名称覆盖预算上限（键为 Agent 名称或 ID 字符串，值为月度预算美元）
    /// </summary>
    public Dictionary<string, decimal> PerAgentBudgets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 预算聚合缓存 TTL（秒），避免每次请求都查询数据库聚合。默认 60 秒。
    /// </summary>
    public int CacheTtlSeconds { get; set; } = 60;
}

/// <summary>
/// BudgetOptions 验证器
/// </summary>
public class BudgetOptionsValidator : OptionsValidatorBase<BudgetOptions>
{
    protected override void ValidateOptions(BudgetOptions options, List<string> errors)
    {
        if (options.DefaultMonthlyBudgetUsd <= 0)
            errors.Add("DefaultMonthlyBudgetUsd must be greater than 0.");

        if (options.WarningThreshold is < 0 or > 1)
            errors.Add("WarningThreshold must be between 0 and 1.");

        if (options.CacheTtlSeconds < 0)
            errors.Add("CacheTtlSeconds must be non-negative.");

        foreach (var (agentKey, budget) in options.PerAgentBudgets)
        {
            if (budget <= 0)
                errors.Add($"PerAgentBudgets['{agentKey}'] must be greater than 0.");
        }
    }
}
