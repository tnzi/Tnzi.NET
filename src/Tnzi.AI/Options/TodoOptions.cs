namespace Tnzi.AI.Options;

/// <summary>
/// Todo/计划模式配置选项
/// </summary>
[ConfigSection("AI:Todo")]
[RuntimeSettingGroup(Key = "ai-conversation", Module = "AI", DisplayName = "Conversation",
    I18nKey = "admin.modules.system.settings.groups.aiConversation", Icon = "mdi:message-cog-outline", Order = 160)]
public class TodoOptions
{
    /// <summary>是否启用 Todo 中间件</summary>
    [RuntimeSetting(Label = "Todo Mode Enabled", I18n = "admin.modules.system.settings.fields.todoEnabled",
        Type = SettingFieldType.Boolean)]
    public bool Enabled { get; set; } = true;

    /// <summary>最大 Todo 数量</summary>
    [RuntimeSetting(Label = "Todo Max Items", I18n = "admin.modules.system.settings.fields.todoMaxItems",
        Type = SettingFieldType.Int, Min = 1, Max = 200)]
    public int MaxItems { get; set; } = 50;
}
