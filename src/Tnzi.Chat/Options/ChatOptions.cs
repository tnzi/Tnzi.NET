namespace Tnzi.Chat.Options;

/// <summary>
/// Chat 模块配置选项
/// 配置路径：Chat
/// </summary>
[ConfigSection("Chat")]
[RuntimeSettingGroup(Key = "chat-general", Module = "Chat", DisplayName = "Chat",
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
    /// 新消息提示音默认开关（客户端默认值；用户仍可按会话静音）。
    /// </summary>
    [RuntimeSetting(Label = "Message Sound", I18n = "admin.modules.system.settings.fields.chatEnableMessageSound",
        Type = SettingFieldType.Boolean)]
    public bool EnableMessageSound { get; set; } = true;

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
