namespace Tnzi.AI.Options;

/// <summary>
/// 后续建议生成配置选项
/// </summary>
[ConfigSection("AI:Suggestions")]
[RuntimeSettingGroup(Key = "ai-conversation", Module = "AI", DisplayName = "AI Conversation",
    I18nKey = "admin.modules.system.settings.groups.aiConversation", Icon = "mdi:message-cog-outline", Order = 160)]
public class SuggestionOptions
{
    /// <summary>是否自动生成后续建议（附加在 AgentRunResult 中）</summary>
    // Suggestions:AutoGenerate 不收录：声明后无任何后端消费者（SuggestionService 只读 Count）
    public bool AutoGenerate { get; set; }

    /// <summary>每次生成的建议数量</summary>
    [RuntimeSetting(Label = "Suggestion Count", I18n = "admin.modules.system.settings.fields.suggestionsCount",
        Type = SettingFieldType.Int, Min = 1, Max = 10)]
    public int Count { get; set; } = 3;

    /// <summary>英文建议最大词数</summary>
    public int MaxWordsEn { get; set; } = 20;

    /// <summary>中文建议最大字符数</summary>
    public int MaxCharsCn { get; set; } = 40;

    /// <summary>用于生成建议的模型（null=使用默认模型）</summary>
    public string? ModelName { get; set; }
}
