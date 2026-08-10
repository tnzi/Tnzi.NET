namespace Tnzi.Identity.Presence.Options;

/// <summary>
/// 用户在线状态（presence）模块配置。
/// 配置路径：Presence
/// </summary>
/// <remarks>
/// <b><c>PermissionGroup = "identity"</c> 不是可省的装饰。</b>配置组派生的一对权限码
/// （<c>{permGroup}.settings.presence.view|update</c>）必须挂到一个<b>真实存在</b>的权限组下，
/// 而缺省值是 <c>Module</c> 归一化后的 <c>"presence"</c> —— 本模块没有、也不需要自己的权限组
/// （它一个 admin 控制器都没有）。挂到不存在的组上，<c>PermissionDbSeeder</c> 会记一行 warning
/// 然后把这两个码<b>丢掉</b>：配置中心里这一组从此只有超管能看能改，角色权限矩阵里连行都没有。
/// 本模块是 Identity 的子模块（共享 <c>Identity_</c> 表前缀），归到 <c>identity</c> 组是它的自然归属，
/// 且 <c>[DependsOn(IdentityModule)]</c> 保证该组永远已被 <c>IdentityPermissions</c> 声明。
/// 同一形态的门禁见 <c>SettingsPermissionGroupResolutionTests</c>。
/// </remarks>
[ConfigSection("Presence")]
[RuntimeSettingGroup(Key = "presence", Module = "Presence", DisplayName = "Presence",
    Icon = "mdi:account-clock-outline", Order = 455, I18nKey = "admin.modules.system.settings.groups.presence",
    PermissionGroup = "identity")]
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
