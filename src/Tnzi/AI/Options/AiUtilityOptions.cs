namespace Tnzi.AI.Options;

/// <summary>
/// AI Utility 全局默认配置，绑定 AI:Utility 配置节
/// </summary>
[ConfigSection("AI:Utility")]
[RuntimeSettingGroup(Key = "ai-general", Module = "AI", DisplayName = "General",
    I18nKey = "admin.modules.system.settings.groups.aiGeneral", Icon = "mdi:robot-outline", Order = 100)]
public class AiUtilityOptions
{
    /// <summary>
    /// 默认模型名称（null = 使用 Provider 默认模型）
    /// </summary>
    [RuntimeSetting(Label = "Utility Model", I18n = "admin.modules.system.settings.fields.utilityModel",
        Description = "Model used by lightweight utility calls (title generation etc.); falls back to the provider default when empty")]
    public string? Model { get; set; }

    /// <summary>
    /// 默认最大输出 Token 数
    /// </summary>
    /// <remarks>
    /// ★ 这是**所有** utility 调用共享的预算，不能按最窄的那个调用点定。
    /// 它曾是 100 —— 按标题生成定的 —— 而框架内四个消费者（RAG 问答、知识图谱抽取、
    /// Agent 评估、建议生成）都不传覆盖，于是输出被静默截断：图谱抽取期望完整 JSON，
    /// 截断后解析失败，产出零实体且不报任何错。
    /// 窄的调用点自己传小值（见 <c>AiUtilityExtensions.GenerateTitleAsync</c>）。
    /// </remarks>
    [RuntimeSetting(Label = "Utility Max Tokens", I18n = "admin.modules.system.settings.fields.utilityMaxTokens",
        Type = SettingFieldType.Int, Min = 1, Max = 100_000)]
    public int MaxTokens { get; set; } = 4096;

    /// <summary>
    /// 默认温度参数；null（默认）= 不发温度，用模型自己的默认值
    /// </summary>
    /// <remarks>
    /// ★ 默认不发是有意的：新一代 Claude 型号对带 <c>temperature</c> 的请求答 400，
    /// 一个对谁都发的默认值会让 utility 调用在这些型号上全部失败。
    /// 配了值时，对收采样参数的型号照常发送；对不收的型号（见 <c>ModelSamplingSupport</c>）不发。
    /// </remarks>
    [RuntimeSetting(Label = "Utility Temperature", I18n = "admin.modules.system.settings.fields.utilityTemperature",
        Description = "Sampling temperature for utility calls; empty uses the model's default. Models that reject temperature never receive it",
        Type = SettingFieldType.Decimal, Min = 0, Max = 2)]
    public double? Temperature { get; set; }
}

/// <summary>
/// IAiUtility.ExecuteAsync 的单次调用覆盖选项
/// </summary>
public class AiUtilityCallOptions
{
    /// <summary>
    /// 覆盖模型
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// 覆盖最大输出 Token 数
    /// </summary>
    public int? MaxTokens { get; init; }

    /// <summary>
    /// 覆盖温度参数
    /// </summary>
    public double? Temperature { get; init; }

    /// <summary>
    /// 本次调用自带的提供商。设置后<b>完全绕过</b> <c>AI:Providers</c>（不要求有任何已启用的配置提供商），
    /// 未设置时行为与此前逐字相同。
    /// </summary>
    /// <remarks>
    /// 模型按 <see cref="Model"/> → <see cref="AiUtilityInlineProvider.DefaultModel"/> 解析；
    /// <see cref="MaxTokens"/> / <see cref="Temperature"/> 未设置时仍回退 <c>AI:Utility</c> 的默认值。
    /// 提供商不合法时本次调用记 Warning 并返回 <see langword="null"/>，与配置提供商的失败形态相同。
    /// <see cref="Services.IAiUtility.IsAvailable"/> 只描述不自带提供商时的解析结果，自带提供商的调用方无需问它。
    /// </remarks>
    public AiUtilityInlineProvider? Provider { get; init; }
}
