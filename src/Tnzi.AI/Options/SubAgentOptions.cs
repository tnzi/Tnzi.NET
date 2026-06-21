namespace Tnzi.AI.Options;

[ConfigSection("AI:SubAgent")]
[RuntimeSettingGroup(Key = "ai-subagent", Module = "AI", DisplayName = "AI Sub-Agents",
    I18nKey = "admin.modules.system.settings.groups.aiSubagent", Icon = "mdi:account-group-outline", Order = 120)]
public class SubAgentOptions
{
    [RuntimeSetting(Label = "Sub-Agents Enabled", I18n = "admin.modules.system.settings.fields.subagentEnabled",
        Type = SettingFieldType.Boolean)]
    public bool Enabled { get; set; } = true;

    [RuntimeSetting(Label = "Max Concurrent", I18n = "admin.modules.system.settings.fields.maxConcurrentSubAgents",
        Type = SettingFieldType.Int, Min = 1, Max = 64)]
    public int MaxConcurrentSubAgents { get; set; } = 3;

    [RuntimeSetting(Label = "Timeout (s)", I18n = "admin.modules.system.settings.fields.timeoutSeconds",
        Type = SettingFieldType.Int, Min = 1, Max = 86_400)]
    public int TimeoutSeconds { get; set; } = 900;

    /// <summary>
    /// 调用链最大深度（根 → 叶，默认 5）。超过此深度拒绝 Spawn。
    /// </summary>
    [RuntimeSetting(Label = "Max Depth", I18n = "admin.modules.system.settings.fields.maxDepth",
        Type = SettingFieldType.Int, Min = 1, Max = 32)]
    public int MaxDepth { get; set; } = 5;

    /// <summary>
    /// 单个根 Run 下最大后代数量（默认 25）。超过此数量拒绝 Spawn。
    /// </summary>
    [RuntimeSetting(Label = "Max Descendants", I18n = "admin.modules.system.settings.fields.maxDescendantsPerRoot",
        Type = SettingFieldType.Int, Min = 1, Max = 1_000)]
    public int MaxDescendantsPerRoot { get; set; } = 25;

    /// <summary>
    /// 全局禁止的工具名称列表（所有 Agent 包括主 Agent 均不可使用）
    /// </summary>
    public List<string> GlobalDisallowedTools { get; set; } = [];

    /// <summary>
    /// 子 Agent 额外禁止的工具名称列表（主 Agent 可用，子 Agent 禁止）
    /// </summary>
    public List<string> SubAgentDisallowedTools { get; set; } = [];

    /// <summary>
    /// 异步/后台 Agent 仅允许的工具名称列表（白名单模式，为空则不限制）
    /// </summary>
    public List<string> AsyncAgentAllowedTools { get; set; } = [];
}
