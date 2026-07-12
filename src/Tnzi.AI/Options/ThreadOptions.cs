namespace Tnzi.AI.Options;

/// <summary>
/// Thread 行为配置，绑定 AI:Thread 配置节
/// </summary>
[ConfigSection("AI:Thread")]
[RuntimeSettingGroup(Key = "ai-general", Module = "AI", DisplayName = "General",
    I18nKey = "admin.modules.system.settings.groups.aiGeneral", Icon = "mdi:robot-outline", Order = 100)]
public class ThreadOptions
{
    /// <summary>
    /// 是否在首轮对话后自动 AI 生成线程标题（默认关闭）
    /// </summary>
    [RuntimeSetting(Label = "Auto-generate Thread Titles", I18n = "admin.modules.system.settings.fields.autoGenerateTitle",
        Type = SettingFieldType.Boolean)]
    public bool AutoGenerateTitle { get; set; }

    /// <summary>
    /// 生成/截取标题的最大字符长度
    /// </summary>
    [RuntimeSetting(Label = "Title Max Length", I18n = "admin.modules.system.settings.fields.titleMaxLength",
        Type = SettingFieldType.Int, Min = 1, Max = 500)]
    public int TitleMaxLength { get; set; } = 50;
}
