namespace Tnzi.AI.Options;

/// <summary>
/// 工具白名单/黑名单 Guardrail 配置
/// </summary>
[ConfigSection("AI:Guardrails:Allowlist")]
[RuntimeSettingGroup(Key = "ai-guardrails", Module = "AI", DisplayName = "Guardrails",
    I18nKey = "admin.modules.system.settings.groups.aiGuardrails", Icon = "mdi:shield-check-outline", Order = 120)]
public class AllowlistGuardrailOptions
{
    /// <summary>
    /// 允许的工具名称列表（空列表 = 允许所有）
    /// </summary>
    public List<string> AllowedTools { get; set; } = [];

    /// <summary>
    /// 禁止的工具名称列表（优先于白名单）
    /// </summary>
    public List<string> DeniedTools { get; set; } = [];

    /// <summary>
    /// 是否精确匹配工具名称（默认 false = 前缀匹配）
    /// </summary>
    [RuntimeSetting(Label = "Match Tool Names Exactly", I18n = "admin.modules.system.settings.fields.guardrailsAllowlistMatchExact",
        Type = SettingFieldType.Boolean, Subsection = "Allowlist",
        Description = "Exact tool-name matching for allow/deny lists (default off = prefix matching)")]
    public bool MatchExact { get; set; }
}
