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
    public void Group_HasOneField()
    {
        Assert.Single(_group.Fields);
    }

    [Fact]
    public void Field_HasCorrectKey()
    {
        var field = _group.Fields[0];
        Assert.Equal("Audit:RetentionDays", field.Key);
    }

    [Fact]
    public void Field_HasCorrectType()
    {
        var field = _group.Fields[0];
        Assert.Equal(SettingFieldType.Int, field.Type);
    }

    [Fact]
    public void DefaultValueAccessor_ReturnsExpectedDefault()
    {
        var field = _group.Fields[0];
        Assert.NotNull(field.DefaultValueAccessor);
        Assert.Equal("90", field.DefaultValueAccessor!());
    }

    [Fact]
    public void Field_HasI18nKey()
    {
        var field = _group.Fields[0];
        Assert.NotNull(field.I18nKey);
    }
}
