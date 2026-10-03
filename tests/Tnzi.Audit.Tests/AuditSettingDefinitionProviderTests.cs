namespace Tnzi.Audit.Tests;

/// <summary>
/// AuditOptions 配置中心特性测试 - 验证 [RuntimeSettingGroup]/[RuntimeSetting] 特性派生的分组符合配置中心契约
/// </summary>
public class AuditSettingDefinitionProviderTests
{
    private readonly SettingDefinitionGroup _group =
        RuntimeSettingMetadataExtractor.Extract(typeof(AuditOptions))!;

    [Fact]
    public void Extract_ReturnsNonNull()
    {
        Assert.NotNull(_group);
    }

    [Fact]
    public void Group_HasExpectedKey()
    {
        Assert.Equal("audit-retention", _group.Key);
    }

    [Fact]
    public void Group_HasExpectedModuleName()
    {
        Assert.Equal("Audit", _group.ModuleName);
    }

    [Fact]
    public void Group_HasExpectedOrder()
    {
        Assert.Equal(600, _group.Order);
    }

    [Fact]
    public void Group_HasExpectedFields()
    {
        // 记录粒度组：RetentionDays + AutoPurgeEnabled + ExportMaxRows + 5 个 Capture 小节字段。
        // EnableEntityAudit 自实体级审计采集管道落地后成为真热配
        // （EntityAuditSaveChangesInterceptor 经 IOptionsMonitor 热读）；
        // ExportMaxRows 由 AuditOperationService 经 IOptionsMonitor 热读（导出超限拒绝）；
        // AutoPurgeEnabled 由 AuditRetentionBackgroundService 每轮热读（开关打开即从下一轮开始删）；
        // EnableResponseResult 仍为"假热配"不暴露（AuditMiddleware 未消费）。
        Assert.Equal(8, _group.Fields.Count);
        Assert.Contains(_group.Fields, f => f.Key == "Audit:EnableOperationAudit");
        Assert.Contains(_group.Fields, f => f.Key == "Audit:EnableEntityAudit");
        Assert.Contains(_group.Fields, f => f.Key == "Audit:RetentionDays");
        Assert.Contains(_group.Fields, f => f.Key == "Audit:AutoPurgeEnabled");
        Assert.Contains(_group.Fields, f => f.Key == "Audit:ExportMaxRows");
        Assert.Contains(_group.Fields, f => f.Key == "Audit:EnableRequestParameters");
        Assert.Contains(_group.Fields, f => f.Key == "Audit:EnableRequestBodyCapture");
        Assert.Contains(_group.Fields, f => f.Key == "Audit:MaxRequestBodySize");
    }

    [Fact]
    public void RetentionDaysField_HasCorrectType()
    {
        var field = _group.Fields.First(f => f.Key == "Audit:RetentionDays");
        Assert.Equal(SettingFieldType.Int, field.Type);
    }

    [Fact]
    public void RetentionDaysField_ReturnsExpectedDefault()
    {
        var field = _group.Fields.First(f => f.Key == "Audit:RetentionDays");
        Assert.NotNull(field.DefaultValueAccessor);
        Assert.Equal("90", field.DefaultValueAccessor!());
    }

    [Fact]
    public void RetentionDaysField_SaysWhetherAnythingIsRemovedAutomatically()
    {
        // 这个字段挂在「Retention」组下，此前没有说明：操作者把 90 改成 30，保存成功、热读拿到新值，
        // 然后什么都不发生 —— 自动删除是 opt-in 的，说明必须把两种语义都写出来。
        var retention = _group.Fields.First(f => f.Key == "Audit:RetentionDays");
        Assert.Contains("Automatic Purge", retention.Description);
        Assert.Contains("admin/audit-operations/expired", retention.Description);

        var autoPurge = _group.Fields.First(f => f.Key == "Audit:AutoPurgeEnabled");
        Assert.Equal(SettingFieldType.Boolean, autoPurge.Type);
        Assert.Equal("False", autoPurge.DefaultValueAccessor!());
    }

    [Fact]
    public void Fields_HaveI18nKeys()
    {
        Assert.All(_group.Fields, f => Assert.NotNull(f.I18nKey));
    }
}
