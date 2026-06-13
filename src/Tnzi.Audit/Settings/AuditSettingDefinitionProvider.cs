namespace Tnzi.Audit.Settings;

/// <summary>
/// Audit 模块内置配置定义 — Audit Retention 组，映射 AuditOptions（配置节 "Audit"）。
/// RetentionDays 在 AuditOperationService.DeleteExpiredOperationsAsync 无参调用时热读取。
/// </summary>
public class AuditSettingDefinitionProvider : ISettingDefinitionProvider
{
    private const string I18nBase = "admin.modules.system.settings";

    public IReadOnlyList<SettingDefinitionGroup> GetGroups() =>
    [
        new SettingDefinitionGroup
        {
            Key = "audit-retention",
            ModuleName = "Audit",
            DisplayName = "Audit Retention",
            I18nKey = $"{I18nBase}.groups.auditRetention",
            Icon = "mdi:archive-clock-outline",
            Order = 600,
            Fields =
            [
                new SettingFieldDefinition
                {
                    Key = "Audit:RetentionDays", Label = "Retention Days", Type = SettingFieldType.Int, Min = 1,
                    I18nKey = $"{I18nBase}.fields.retentionDays",
                    DefaultValueAccessor = () => new AuditOptions().RetentionDays.ToString(CultureInfo.InvariantCulture),
                },
            ],
        },
    ];
}
