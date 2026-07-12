namespace Tnzi.AI.Options;

/// <summary>
/// 配额默认值配置选项
/// </summary>
[ConfigSection("AI:Quota")]
[RuntimeSettingGroup(Key = "ai-quota", Module = "AI", DisplayName = "Quota",
    I18nKey = "admin.modules.system.settings.groups.aiQuota", Icon = "mdi:counter", Order = 180)]
public class QuotaOptions
{
    /// <summary>
    /// 新用户默认每日 Token 限额（默认 100 万）
    /// </summary>
    [RuntimeSetting(Label = "Default Daily Token Limit", I18n = "admin.modules.system.settings.fields.quotaDefaultDailyTokenLimit",
        Type = SettingFieldType.Int, Min = 0,
        Description = "Daily token limit assigned to a new user's quota")]
    public long DefaultDailyTokenLimit { get; set; } = 1_000_000;

    /// <summary>
    /// 新用户默认每月 Token 限额（默认 2000 万）
    /// </summary>
    [RuntimeSetting(Label = "Default Monthly Token Limit", I18n = "admin.modules.system.settings.fields.quotaDefaultMonthlyTokenLimit",
        Type = SettingFieldType.Int, Min = 0,
        Description = "Monthly token limit assigned to a new user's quota")]
    public long DefaultMonthlyTokenLimit { get; set; } = 20_000_000;
}
