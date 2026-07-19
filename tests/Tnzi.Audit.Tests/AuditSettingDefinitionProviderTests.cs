namespace Tnzi.Audit.Tests;

/// <summary>
/// AuditOptions 配置中心特性测试 — 验证 [RuntimeSettingGroup]/[RuntimeSetting] 特性派生的分组符合配置中心契约
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
        // 记录粒度组：RetentionDays + 5 个 Capture 小节字段。
        // EnableEntityAudit 自实体级审计采集管道落地后成为真热配
        // （EntityAuditSaveChangesInterceptor 经 IOptionsMonitor 热读）；
        // EnableResponseResult 仍为"假热配"不暴露（AuditMiddleware 未消费）。
        Assert.Equal(6, _group.Fields.Count);
        Assert.Contains(_group.Fields, f => f.Key == "Audit:EnableOperationAudit");
        Assert.Contains(_group.Fields, f => f.Key == "Audit:EnableEntityAudit");
        Assert.Contains(_group.Fields, f => f.Key == "Audit:RetentionDays");
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
    public void Fields_HaveI18nKeys()
    {
        Assert.All(_group.Fields, f => Assert.NotNull(f.I18nKey));
    }
}
