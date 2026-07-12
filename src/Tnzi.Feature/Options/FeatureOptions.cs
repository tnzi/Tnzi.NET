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
    /// <summary>
    /// Whether to enable the feature module
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Cache refresh interval in minutes (0 = no auto-refresh)
    /// </summary>
    [RuntimeSetting(Label = "Cache Refresh Interval (minutes)", I18n = "admin.modules.system.settings.fields.featureCacheRefreshInterval",
        Type = SettingFieldType.Int, Min = 0,
        Description = "How often the feature-definition snapshot is refreshed, in minutes. Set to 0 to disable auto-refresh (snapshot persists until explicitly invalidated).")]
    public int CacheRefreshIntervalMinutes { get; set; } = 30;
}
