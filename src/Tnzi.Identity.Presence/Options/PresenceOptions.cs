namespace Tnzi.Identity.Presence.Options;

/// <summary>
/// 用户在线状态（presence）模块配置。
/// 配置路径：Presence
/// </summary>
[ConfigSection("Presence")]
[RuntimeSettingGroup(Key = "presence", Module = "Presence", DisplayName = "Presence",
    Icon = "mdi:account-clock-outline", Order = 455, I18nKey = "admin.modules.system.settings.groups.presence")]
public class PresenceOptions
{
    /// <summary>
    /// 是否启用在线状态展示（状态点/状态切换器/头像圆点）。关闭后前端隐藏在线状态相关 UI。
    /// </summary>
    [RuntimeSetting(Label = "Enable Presence", I18n = "admin.modules.system.settings.fields.presenceEnabled",
        Type = SettingFieldType.Boolean)]
    public bool EnablePresence { get; set; } = true;

    /// <summary>
    /// 是否允许用户设置"隐身"状态（对外显示离线）。关闭后前端隐藏隐身选项，
    /// 服务端拒绝隐身意图，且历史隐身意图按在线解析——面向不希望员工隐身的部署。
    /// </summary>
    [RuntimeSetting(Label = "Allow Invisible Status", I18n = "admin.modules.system.settings.fields.presenceAllowInvisible",
        Type = SettingFieldType.Boolean)]
    public bool AllowInvisible { get; set; } = true;

    /// <summary>
    /// 是否启用"闲置一定时间自动切换到离开(Away)"。默认开启。客户端按 <see cref="AutoAwayMinutes"/>
    /// 计时，越过阈值上报空闲，服务端把在线用户的有效状态解析为 Away；恢复活动后自动切回。
    /// </summary>
    [RuntimeSetting(Label = "Auto Away", I18n = "admin.modules.system.settings.fields.presenceAutoAwayEnabled",
        Type = SettingFieldType.Boolean)]
    public bool AutoAwayEnabled { get; set; } = true;

    /// <summary>
    /// 无操作多少分钟后自动切换到离开（供客户端本地空闲计时；运行时可调，最小 1）。
    /// </summary>
    [RuntimeSetting(Label = "Auto Away Minutes", I18n = "admin.modules.system.settings.fields.presenceAutoAwayMinutes",
        Type = SettingFieldType.Int, Min = 1)]
    public int AutoAwayMinutes { get; set; } = 15;
}

/// <summary>
/// Presence 配置验证器
/// </summary>
public class PresenceOptionsValidator : OptionsValidatorBase<PresenceOptions>
{
    protected override void ValidateOptions(PresenceOptions options, List<string> errors)
    {
        if (options.AutoAwayMinutes < 1)
            errors.Add("AutoAwayMinutes must be at least 1.");
    }
}
