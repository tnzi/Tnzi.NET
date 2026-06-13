namespace Tnzi.Notification.Settings;

/// <summary>
/// Notification 模块内置配置定义 — Notification 组，映射 NotificationOptions（配置节 "Notification"）。
/// 全部字段经 IOptionsMonitor.CurrentValue 运行时消费（NotificationService / NotificationRetryService）。
/// </summary>
public class NotificationSettingDefinitionProvider : ISettingDefinitionProvider
{
    private const string I18nBase = "admin.modules.system.settings";

    public IReadOnlyList<SettingDefinitionGroup> GetGroups() =>
    [
        new SettingDefinitionGroup
        {
            Key = "notification-general",
            ModuleName = "Notification",
            DisplayName = "Notification",
            I18nKey = $"{I18nBase}.groups.notificationGeneral",
            Icon = "mdi:bell-cog-outline",
            Order = 400,
            Fields =
            [
                new SettingFieldDefinition
                {
                    Key = "Notification:SendTimeoutSeconds", Label = "Send Timeout (s)", Type = SettingFieldType.Int, Min = 1,
                    I18nKey = $"{I18nBase}.fields.sendTimeoutSeconds",
                    DefaultValueAccessor = () => new NotificationOptions().SendTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
                },
                new SettingFieldDefinition
                {
                    Key = "Notification:Retry:RetryDelaySeconds", Label = "Retry Delay (s)", Type = SettingFieldType.Int, Min = 0,
                    I18nKey = $"{I18nBase}.fields.retryDelaySeconds",
                    DefaultValueAccessor = () => new RetryOptions().RetryDelaySeconds.ToString(CultureInfo.InvariantCulture),
                },
                new SettingFieldDefinition
                {
                    Key = "Notification:Retry:EnableExponentialBackoff", Label = "Exponential Backoff", Type = SettingFieldType.Boolean,
                    I18nKey = $"{I18nBase}.fields.enableExponentialBackoff",
                    DefaultValueAccessor = () => new RetryOptions().EnableExponentialBackoff.ToString().ToLowerInvariant(),
                },
            ],
        },
    ];
}
