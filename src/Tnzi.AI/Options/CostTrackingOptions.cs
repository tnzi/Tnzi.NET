namespace Tnzi.AI.Options;

/// <summary>
/// 成本追踪配置选项
/// </summary>
[ConfigSection("AI:CostTracking")]
[RuntimeSettingGroup(Key = "ai-budget", Module = "AI", DisplayName = "Budget",
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
    /// 模型成本率，按 provider → model 两级：<c>ModelCosts["OpenAI"]["gpt-4o"]</c>；
    /// model 位可用 <c>"*"</c> 作该 provider 的通配。
    /// </summary>
    /// <remarks>
    /// <para>查找顺序: 精确匹配 provider/model → 通配 provider/"*" → <see cref="DefaultCostRate"/> → null。</para>
    /// <para>
    /// appsettings 里两种写法等价：嵌套 <c>"OpenAI": { "gpt-4o": { … } }</c>，或扁平键 <c>"OpenAI:gpt-4o": { … }</c>
    /// （<c>:</c> 是 IConfiguration 的路径分隔符，扁平键本来就会被拆成两级）。
    /// </para>
    /// <para>
    /// ★ 不能是 <c>Dictionary&lt;string, ModelCostRate&gt;</c> 配 "provider:model" 键。2026-09-12 之前就是那样：
    /// 绑定器把 <c>"OpenAI:gpt-4o"</c> 拆成 <c>ModelCosts:OpenAI:gpt-4o</c>，键 <c>OpenAI</c> 得到一个全 0 的费率、
    /// <c>gpt-4o</c> 被当未知属性丢弃 —— 从 appsettings 里从来没有一条费率绑进来过，而所有测试都用 C# 直接构造字典。
    /// 绑定器建出来的内层字典不带比较器，查找方（<c>CostCalculator</c>）自己做大小写不敏感匹配。
    /// </para>
    /// </remarks>
    public Dictionary<string, Dictionary<string, ModelCostRate>> ModelCosts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

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
