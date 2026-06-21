namespace Tnzi.AI.Options;

/// <summary>
/// 成本追踪配置选项
/// </summary>
[ConfigSection("AI:CostTracking")]
[RuntimeSettingGroup(Key = "ai-budget", Module = "AI", DisplayName = "AI Budget",
    I18nKey = "admin.modules.system.settings.groups.aiBudget", Icon = "mdi:cash-multiple", Order = 110)]
public class CostTrackingOptions
{
    /// <summary>
    /// 是否启用成本追踪（默认关闭）
    /// </summary>
    [RuntimeSetting(Label = "Cost Tracking Enabled", I18n = "admin.modules.system.settings.fields.costTrackingEnabled",
        Type = SettingFieldType.Boolean,
        Description = "Enable per-request token cost calculation (requires model cost rates configured)")]
    public bool Enabled { get; set; }

    /// <summary>
    /// 模型成本率字典，键格式: "provider:model"（如 "OpenAI:gpt-4o"）
    /// </summary>
    /// <remarks>
    /// 查找顺序: 精确匹配 "provider:model" → 通配匹配 "provider:*" → 无匹配返回 null
    /// </remarks>
    public Dictionary<string, ModelCostRate> ModelCosts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 默认成本率（当 ModelCosts 中无匹配时使用，为 null 则不计算成本）
    /// </summary>
    public ModelCostRate? DefaultCostRate { get; set; }
}

/// <summary>
/// 模型成本率（美元/百万 Token）
/// </summary>
public class ModelCostRate
{
    /// <summary>
    /// 输入 Token 成本（美元/百万 Token）
    /// </summary>
    public decimal InputCostPer1MTokens { get; set; }

    /// <summary>
    /// 输出 Token 成本（美元/百万 Token）
    /// </summary>
    public decimal OutputCostPer1MTokens { get; set; }

    /// <summary>
    /// 缓存输入 Token 成本（美元/百万 Token，可选，通常为正常输入成本的 10-50%）
    /// </summary>
    public decimal? CachedInputCostPer1MTokens { get; set; }
}
