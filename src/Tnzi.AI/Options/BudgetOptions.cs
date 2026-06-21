namespace Tnzi.AI.Options;

/// <summary>
/// USD 成本预算配置选项
/// </summary>
[ConfigSection("AI:Budget")]
[RuntimeSettingGroup(Key = "ai-budget", Module = "AI", DisplayName = "AI Budget",
    I18nKey = "admin.modules.system.settings.groups.aiBudget", Icon = "mdi:cash-multiple", Order = 110)]
public class BudgetOptions
{
    /// <summary>
    /// 是否启用 USD 预算管控（默认关闭）
    /// </summary>
    [RuntimeSetting(Label = "Budget Enabled", I18n = "admin.modules.system.settings.fields.budgetEnabled",
        Type = SettingFieldType.Boolean)]
    public bool Enabled { get; set; }

    /// <summary>
    /// 默认每月预算上限（美元，默认 100）
    /// </summary>
    [RuntimeSetting(Label = "Default Monthly Budget (USD)", I18n = "admin.modules.system.settings.fields.defaultMonthlyBudgetUsd",
        Type = SettingFieldType.Decimal, Min = 0)]
    public decimal DefaultMonthlyBudgetUsd { get; set; } = 100m;

    /// <summary>
    /// 预警阈值（0-1，达到该比例时触发 Warning，默认 0.8）
    /// </summary>
    [RuntimeSetting(Label = "Warning Threshold", I18n = "admin.modules.system.settings.fields.warningThreshold",
        Type = SettingFieldType.Decimal, Min = 0, Max = 1)]
    public double WarningThreshold { get; set; } = 0.8;

    /// <summary>
    /// 按 Agent 名称覆盖预算上限（键为 Agent 名称或 ID 字符串，值为月度预算美元）
    /// </summary>
    public Dictionary<string, decimal> PerAgentBudgets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 预算聚合缓存 TTL（秒），避免每次请求都查询数据库聚合。默认 60 秒。
    /// </summary>
    [RuntimeSetting(Label = "Cache TTL (s)", I18n = "admin.modules.system.settings.fields.cacheTtlSeconds",
        Type = SettingFieldType.Int, Min = 0, Max = 86_400)]
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
