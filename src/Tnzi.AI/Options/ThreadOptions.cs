namespace Tnzi.AI.Options;

/// <summary>
/// Thread 行为配置，绑定 AI:Thread 配置节
/// </summary>
/// <remarks>
/// 线程标题分两步：第一条用户消息被接受的那一刻写回退标题（取消息开头几个词，确定性、不调模型），
/// 开了 <see cref="AutoGenerateTitle"/> 时同时在后台生成 AI 标题替换它。两者都不等回复。
/// 用户改过名的线程不再被自动标题替换。
/// </remarks>
[ConfigSection("AI:Thread")]
[RuntimeSettingGroup(Key = "ai-general", Module = "AI", DisplayName = "General",
    I18nKey = "admin.modules.system.settings.groups.aiGeneral", Icon = "mdi:robot-outline", Order = 100)]
public class ThreadOptions
{
    /// <summary>
    /// 是否在第一条用户消息被接受后用 AI 生成线程标题（默认关闭；关闭时只有回退标题）
    /// </summary>
    [RuntimeSetting(Label = "Auto-generate Thread Titles", I18n = "admin.modules.system.settings.fields.autoGenerateTitle",
        Description = "Name new conversations with the title model in the background, from the first user message",
        Type = SettingFieldType.Boolean)]
    public bool AutoGenerateTitle { get; set; }

    /// <summary>
    /// 标题的目标词数（空格分词的语言）。回退标题取第一条消息的前这么多个词，AI 标题也按它要求长度。
    /// </summary>
    [RuntimeSetting(Label = "Title Target Words", I18n = "admin.modules.system.settings.fields.titleTargetWords",
        Description = "About how many words a title has in languages written with spaces",
        Type = SettingFieldType.Int, Min = 1, Max = 20)]
    public int TitleTargetWords { get; set; } = 5;

    /// <summary>
    /// 标题的目标字数（中日韩文字）。回退标题与 AI 标题对 CJK 文字都按它计长度。
    /// </summary>
    [RuntimeSetting(Label = "Title Target CJK Characters", I18n = "admin.modules.system.settings.fields.titleTargetCjkCharacters",
        Description = "About how many Chinese, Japanese or Korean characters a title has",
        Type = SettingFieldType.Int, Min = 2, Max = 40)]
    public int TitleTargetCjkCharacters { get; set; } = 10;

    /// <summary>
    /// 交给标题模型的第一条消息最多多少字节（UTF-8）。粘贴一整份文档时只送开头这一段。
    /// </summary>
    [RuntimeSetting(Label = "Title Max Input Bytes", I18n = "admin.modules.system.settings.fields.titleMaxInputBytes",
        Description = "How much of the first message (UTF-8 bytes) goes to the title model",
        Type = SettingFieldType.Int, Min = 256, Max = 65536)]
    public int TitleMaxInputBytes { get; set; } = 4096;

    /// <summary>
    /// 标题最多多少个字符（按用户看到的字符计，emoji 与组合字符算一个）。回退标题与 AI 标题都截到这个长度。
    /// </summary>
    /// <remarks>
    /// 按字符而不是字节：同样 80 字节，英文是 80 个字母、中文只有 26 个字，管理员填的数字应当对两种文字意思一样。
    /// </remarks>
    [RuntimeSetting(Label = "Title Max Length", I18n = "admin.modules.system.settings.fields.titleMaxLength",
        Description = "Longest title, in characters; longer titles are cut",
        Type = SettingFieldType.Int, Min = 10, Max = 200)]
    public int TitleMaxLength { get; set; } = 50;

    /// <summary>
    /// 生成标题用的模型；为空时用 <c>AI:Utility:Model</c>（再为空则用提供商默认模型）。
    /// </summary>
    [RuntimeSetting(Label = "Title Model", I18n = "admin.modules.system.settings.fields.titleModel",
        Description = "Model that names conversations; empty uses the utility model")]
    public string? TitleModel { get; set; }
}
