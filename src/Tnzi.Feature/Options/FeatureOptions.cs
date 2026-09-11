namespace Tnzi.Feature.Options;

/// <summary>
/// Feature module configuration options
/// </summary>
[ConfigSection("Feature")]
[RuntimeSettingGroup(Key = "feature-general", Module = "Feature", DisplayName = "Feature Management",
    I18nKey = "admin.modules.system.settings.groups.featureGeneral",
    Icon = "mdi:toggle-switch-outline", Order = 460, PermissionGroup = "system")]
public class FeatureOptions
{
    // 刻意没有 `Enabled` 总开关：它曾经存在但全仓零消费者 —— 配成 false 一切照旧，
    // 定义照常解析、开关照常评估、用量照常写。一个什么都不做的 `Enabled` 在配置面上就是一句谎；
    // 不加载 FeatureModule 才是「关闭功能开关模块」的方式。

    /// <summary>
    /// Cache refresh interval in minutes (0 = no auto-refresh)
    /// </summary>
    [RuntimeSetting(Label = "Cache Refresh Interval (minutes)", I18n = "admin.modules.system.settings.fields.featureCacheRefreshInterval",
        Type = SettingFieldType.Int, Min = 0,
        Description = "How often the feature-definition snapshot is refreshed, in minutes. Set to 0 to disable auto-refresh (snapshot persists until explicitly invalidated).")]
    public int CacheRefreshIntervalMinutes { get; set; } = 30;

    /// <summary>
    /// How long a resolved feature value is cached per scope, in seconds (0 = no value cache).
    /// </summary>
    /// <remarks>
    /// 没有它时每次 <c>IsEnabledAsync</c> 都让两个内置 provider 各查一次库。写入经事件在本实例立刻失效；
    /// 其它实例最多滞后一个 TTL。经 <see cref="FeatureValueCache"/> 热读。
    /// </remarks>
    [RuntimeSetting(Label = "Value Cache Duration (seconds)", I18n = "admin.modules.system.settings.fields.featureValueCacheSeconds",
        Type = SettingFieldType.Int, Min = 0,
        Description = "How long a resolved feature value is cached per scope, in seconds. 0 disables the cache and every check queries the database. Writes through the admin API invalidate the entry on this instance immediately; other instances pick the change up when their entry expires.")]
    public int ValueCacheSeconds { get; set; } = 60;

    /// <summary>
    /// Whether every feature check is recorded for the usage analytics tab.
    /// </summary>
    /// <remarks>
    /// 记录进内存队列后由后台成批落库，不在请求路径上写库；关掉后连队列都不进。
    /// 经 <see cref="FeatureUsageService"/> 热读。
    /// </remarks>
    [RuntimeSetting(Label = "Usage Tracking", I18n = "admin.modules.system.settings.fields.featureUsageTrackingEnabled",
        Type = SettingFieldType.Boolean,
        Description = "Record every feature check for the usage analytics tab. Records are queued and written in batches off the request path; turn this off to stop the usage table from growing on hot endpoints.")]
    public bool UsageTrackingEnabled { get; set; } = true;
}
