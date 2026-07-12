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
    /// 是否启用用户在线状态展示（状态点/状态切换器）。
    /// </summary>
    [RuntimeSetting(Label = "Enable Presence", I18n = "admin.modules.system.settings.fields.chatEnablePresence",
        Type = SettingFieldType.Boolean)]
    public bool EnablePresence { get; set; } = true;

    /// <summary>
    /// 是否允许用户设置"隐身"状态（对外显示离线）。关闭后前端隐藏隐身选项，
    /// 服务端拒绝隐身意图，且历史隐身意图按在线解析——面向不希望员工隐身的部署。
    /// </summary>
    [RuntimeSetting(Label = "Allow Invisible Status", I18n = "admin.modules.system.settings.fields.chatAllowInvisible",
        Type = SettingFieldType.Boolean)]
    public bool AllowInvisible { get; set; } = true;

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
