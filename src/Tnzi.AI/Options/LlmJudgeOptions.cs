namespace Tnzi.AI.Options;

/// <summary>
/// LLM-as-Judge guardrail 配置
/// </summary>
[ConfigSection("AI:Guardrails:LlmJudge")]
[RuntimeSettingGroup(Key = "ai-guardrails", Module = "AI", DisplayName = "Guardrails",
    I18nKey = "admin.modules.system.settings.groups.aiGuardrails", Icon = "mdi:shield-check-outline", Order = 120)]
public class LlmJudgeOptions
{
    /// <summary>是否启用 LLM 评估</summary>
    [RuntimeSetting(Label = "LLM Judge Enabled", I18n = "admin.modules.system.settings.fields.guardrailsLlmJudgeEnabled",
        Type = SettingFieldType.Boolean, Subsection = "LLM Judge")]
    public bool Enabled { get; set; }

    /// <summary>LLM 提供者名称（null 使用默认）</summary>
    [RuntimeSetting(Label = "Judge Provider", I18n = "admin.modules.system.settings.fields.guardrailsLlmJudgeProvider",
        Subsection = "LLM Judge", Description = "Provider used for the judge model (empty = default provider)")]
    public string? Provider { get; set; }

    /// <summary>LLM 模型 ID</summary>
    [RuntimeSetting(Label = "Judge Model", I18n = "admin.modules.system.settings.fields.guardrailsLlmJudgeModel",
        Subsection = "LLM Judge")]
    public string? Model { get; set; }

    /// <summary>输入评估系统提示词</summary>
    [RuntimeSetting(Label = "Input Judge Prompt", I18n = "admin.modules.system.settings.fields.guardrailsLlmJudgeInputPrompt",
        Type = SettingFieldType.Text, Subsection = "LLM Judge")]
    public string? InputJudgePrompt { get; set; }

    /// <summary>输出评估系统提示词</summary>
    [RuntimeSetting(Label = "Output Judge Prompt", I18n = "admin.modules.system.settings.fields.guardrailsLlmJudgeOutputPrompt",
        Type = SettingFieldType.Text, Subsection = "LLM Judge")]
    public string? OutputJudgePrompt { get; set; }

    /// <summary>工具调用评估系统提示词（工具级 IGuardrailProvider 评估时使用）</summary>
    [RuntimeSetting(Label = "Tool Judge Prompt", I18n = "admin.modules.system.settings.fields.guardrailsLlmJudgeToolPrompt",
        Type = SettingFieldType.Text, Subsection = "LLM Judge")]
    public string? ToolJudgePrompt { get; set; }

    /// <summary>超时秒数</summary>
    [RuntimeSetting(Label = "Judge Timeout (s)", I18n = "admin.modules.system.settings.fields.guardrailsLlmJudgeTimeoutSeconds",
        Type = SettingFieldType.Int, Min = 1, Max = 600, Subsection = "LLM Judge")]
    public int TimeoutSeconds { get; set; } = 30;
}
