namespace Tnzi.Chat.Options;

/// <summary>
/// Chat 模块配置选项
/// 配置路径：Chat
/// </summary>
[ConfigSection("Chat")]
[RuntimeSettingGroup(Key = "chat-general", Module = "Chat", DisplayName = "General",
    Icon = "mdi:chat-outline", Order = 450, I18nKey = "admin.modules.system.settings.groups.chatGeneral")]
public class ChatOptions
{
    /// <summary>
    /// 是否启用群聊功能。关闭后服务端拒绝建群/加人等写操作，前端隐藏建群入口。
    /// </summary>
    [RuntimeSetting(Label = "Enable Groups", I18n = "admin.modules.system.settings.fields.chatEnableGroups",
        Type = SettingFieldType.Boolean)]
    public bool EnableGroups { get; set; } = true;

    /// <summary>
    /// 单个群的最大成员数（0 = 不限制）。
    /// </summary>
    [RuntimeSetting(Label = "Max Group Members", I18n = "admin.modules.system.settings.fields.chatMaxGroupMembers",
        Type = SettingFieldType.Int, Min = 0)]
    public int MaxGroupMembers { get; set; } = 200;

    /// <summary>
    /// 群头像拼合选取的成员数上限（1-9，微信式九宫格；群主恒第一，其余按入群顺序取前 N 个）。
    /// </summary>
    [RuntimeSetting(Label = "Group Avatar Members", I18n = "admin.modules.system.settings.fields.chatGroupAvatarMemberCount",
        Type = SettingFieldType.Int, Min = 1, Max = 9)]
    public int GroupAvatarMemberCount { get; set; } = 9;

    /// <summary>
    /// 消息提示音总开关（关闭后所有聊天音效均不播放；用户仍可按会话静音）。
    /// </summary>
    [RuntimeSetting(Label = "Message Sound", I18n = "admin.modules.system.settings.fields.chatEnableMessageSound",
        Type = SettingFieldType.Boolean)]
    public bool EnableMessageSound { get; set; } = true;

    /// <summary>
    /// 通知音效：窗口关闭或消息来自非当前会话时播放（较长、引人注意）。<c>None</c> = 该类别静音。
    /// </summary>
    [RuntimeSetting(Label = "Notification Sound", I18n = "admin.modules.system.settings.fields.chatNotificationSound",
        Type = SettingFieldType.Select)]
    public ChatSoundEffect NotificationSound { get; set; } = ChatSoundEffect.Chime;

    /// <summary>
    /// 会话内音效：正在与对方对话时收发消息播放（短促、平和，仅作体验反馈）。<c>None</c> = 该类别静音。
    /// </summary>
    [RuntimeSetting(Label = "In-Conversation Sound", I18n = "admin.modules.system.settings.fields.chatMessageSound",
        Type = SettingFieldType.Select)]
    public ChatSoundEffect MessageSound { get; set; } = ChatSoundEffect.Pop;

    /// <summary>
    /// 新消息且窗口关闭时，启动器图标的提醒动效（引起注意）。<c>None</c> = 不做动效（仍保留未读徽标）。
    /// </summary>
    [RuntimeSetting(Label = "New Message Effect", I18n = "admin.modules.system.settings.fields.chatNewMessageEffect",
        Type = SettingFieldType.Select)]
    public ChatNewMessageEffect NewMessageEffect { get; set; } = ChatNewMessageEffect.Shake;

    /// <summary>
    /// 新消息且浏览器标签页未聚焦时，闪烁标签页标题以引起注意（用户切回聚焦后自动恢复）。
    /// </summary>
    [RuntimeSetting(Label = "Flash Tab Title", I18n = "admin.modules.system.settings.fields.chatFlashTitle",
        Type = SettingFieldType.Boolean)]
    public bool FlashTitleOnMessage { get; set; } = true;

    /// <summary>
    /// 是否允许发送图片/文件消息。关闭后服务端拒绝媒体消息，前端隐藏附件入口。
    /// </summary>
    [RuntimeSetting(Label = "File Messages", I18n = "admin.modules.system.settings.fields.chatEnableFileMessages",
        Type = SettingFieldType.Boolean)]
    public bool EnableFileMessages { get; set; } = true;

    /// <summary>
    /// 联系人目录/搜索单页返回上限。
    /// </summary>
    [RuntimeSetting(Label = "Contact Search Limit", I18n = "admin.modules.system.settings.fields.chatContactSearchLimit",
        Type = SettingFieldType.Int, Min = 1)]
    public int ContactSearchLimit { get; set; } = 20;
}

/// <summary>
/// Chat 配置验证器
/// </summary>
public class ChatOptionsValidator : OptionsValidatorBase<ChatOptions>
{
    protected override void ValidateOptions(ChatOptions options, List<string> errors)
    {
        if (options.MaxGroupMembers < 0)
            errors.Add("MaxGroupMembers must be greater than or equal to 0 (0 = unlimited).");

        if (options.MaxGroupMembers > 0 && options.MaxGroupMembers < 2)
            errors.Add("MaxGroupMembers must be at least 2 when limited (a group needs the owner plus one member).");

        if (options.GroupAvatarMemberCount < 1 || options.GroupAvatarMemberCount > 9)
            errors.Add("GroupAvatarMemberCount must be between 1 and 9.");

        if (options.ContactSearchLimit <= 0)
            errors.Add("ContactSearchLimit must be greater than 0.");
    }
}

// 配置值枚举与其唯一 owner ChatOptions 同文件共置（config-value enum co-located with its owning
// options class，对齐框架 AI 的 Options/ThinkingOptions.cs 范式，见 docs/coding-standards/metadata.md）。

/// <summary>
/// 聊天消息音效预设。前端用 WebAudio 实时合成对应音效（无二进制资源、无外部请求）。
/// 分两个家族：
/// <list type="bullet">
/// <item><b>Attention（通知型）</b>：较长、多音、引人注意——用于窗口关闭或非当前会话收到消息。</item>
/// <item><b>Subtle（对话型）</b>：短促、平和、低音量——用于当前会话内收发消息，仅作体验反馈。</item>
/// </list>
/// <c>None</c> = 该类别静音。
/// </summary>
public enum ChatSoundEffect
{
    /// <summary>静音（该类别不播放）。</summary>
    None = 0,

    // ── Attention（通知型：较长、引人注意）────────────────────────────
    /// <summary>钟琴：两声下行铃音，温暖经典（默认通知音）。</summary>
    Chime = 1,
    /// <summary>叮咚：门铃式下行两声，熟悉的到达提示。</summary>
    DingDong = 2,
    /// <summary>三连音：三声上行琶音，明亮清脆。</summary>
    TriTone = 3,
    /// <summary>马林巴：三声木琴琶音，柔和活泼。</summary>
    Marimba = 4,
    /// <summary>脉冲：两声同音短促提示，醒目直接。</summary>
    Pulse = 5,
    /// <summary>铃：单声撞钟带长衰减尾音，优雅。</summary>
    Bell = 6,

    // ── Subtle（对话型：短促、平和）──────────────────────────────────
    /// <summary>气泡：单声柔和下滑气泡音（默认会话音）。</summary>
    Pop = 7,
    /// <summary>轻点：极短高频轻响，极简。</summary>
    Tick = 8,
    /// <summary>轻鸣：短促中频单音，中性。</summary>
    Blip = 9,
    /// <summary>柔和：略缓起音的低音量单音，平静。</summary>
    Soft = 10,
    /// <summary>水滴：短促上下滑音，悦耳。</summary>
    Drop = 11,
}

/// <summary>
/// 新消息且聊天窗口关闭时，启动器图标（header 聊天入口）的视觉提醒动效。
/// 借鉴主流 IM 的"引起注意"手法（微信/QQ 桌面端图标晃动、macOS Dock 弹跳、MSN 闪烁）。
/// 纯 CSS 动画，短暂播放一次；<c>None</c> = 不做动效（仍保留未读徽标）。
/// </summary>
public enum ChatNewMessageEffect
{
    /// <summary>不做动效（仅未读徽标）。</summary>
    None = 0,

    /// <summary>晃动：图标左右摇摆（默认，微信/QQ 桌面端手法）。</summary>
    Shake = 1,

    /// <summary>脉冲：图标缩放一次并带光环扩散。</summary>
    Pulse = 2,

    /// <summary>闪烁：图标短暂闪烁并高亮主题色（经典 MSN/QQ 手法）。</summary>
    Blink = 3,

    /// <summary>弹跳：图标上下弹跳（macOS Dock 手法）。</summary>
    Bounce = 4,
}
