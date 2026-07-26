namespace Tnzi.AI.Options;

/// <summary>
/// 工具结果预算配置 - 截断超限的工具结果，防止上下文膨胀
/// </summary>
[ConfigSection("AI:ToolResultBudget")]
[RuntimeSettingGroup(Key = "ai-tools", Module = "AI", DisplayName = "Tools",
    I18nKey = "admin.modules.system.settings.groups.aiTools", Icon = "mdi:tools", Order = 130)]
public class ToolResultBudgetOptions
{
    /// <summary>
    /// 是否启用工具结果截断。默认 true。
    /// </summary>
    [RuntimeSetting(Label = "Tool Result Budget Enabled", I18n = "admin.modules.system.settings.fields.toolResultBudgetEnabled",
        Type = SettingFieldType.Boolean, Subsection = "Result Budget")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 单个工具结果的最大字符数。超过此限制的结果将被截断。默认 30000。
    /// </summary>
    [RuntimeSetting(Label = "Max Result Chars", I18n = "admin.modules.system.settings.fields.toolResultBudgetMaxResultChars",
        Type = SettingFieldType.Int, Min = 0, Subsection = "Result Budget",
        Description = "Truncate any single tool result longer than this many characters")]
    public int MaxResultChars { get; set; } = 30_000;

    /// <summary>
    /// 截断时保留的预览字符数（结果开头部分）。默认 2000。
    /// </summary>
    [RuntimeSetting(Label = "Preview Chars", I18n = "admin.modules.system.settings.fields.toolResultBudgetPreviewChars",
        Type = SettingFieldType.Int, Min = 0, Subsection = "Result Budget",
        Description = "Characters of the result head kept when truncating")]
    public int PreviewChars { get; set; } = 2_000;
}
