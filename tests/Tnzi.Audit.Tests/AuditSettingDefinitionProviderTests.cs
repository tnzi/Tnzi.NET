namespace Tnzi.Audit.Tests;

/// <summary>
/// AuditSettingDefinitionProvider 结构测试 — 验证 provider 注册字段符合配置中心契约
/// </summary>
public class AuditSettingDefinitionProviderTests
{
    private readonly AuditSettingDefinitionProvider _provider = new();

    [Fact]
    public void GetGroups_ReturnsOneGroup()
    {
        var groups = _provider.GetGroups();
        Assert.Single(groups);
    }

    [Fact]
    public void Group_HasExpectedKey()
    {
        var group = _provider.GetGroups()[0];
        Assert.Equal("audit-retention", group.Key);
    }

    [Fact]
    public void Group_HasExpectedModuleName()
    {
        var group = _provider.GetGroups()[0];
        Assert.Equal("Audit", group.ModuleName);
    }

    [Fact]
    public void Group_HasExpectedOrder()
    {
        var group = _provider.GetGroups()[0];
        Assert.Equal(600, group.Order);
    }

    [Fact]
    public void Group_HasOneField()
    {
        var group = _provider.GetGroups()[0];
        Assert.Single(group.Fields);
    }

    [Fact]
    public void Field_HasCorrectKey()
    {
        var field = _provider.GetGroups()[0].Fields[0];
        Assert.Equal("Audit:RetentionDays", field.Key);
    }

    [Fact]
    public void Field_HasCorrectType()
    {
        var field = _provider.GetGroups()[0].Fields[0];
        Assert.Equal(SettingFieldType.Int, field.Type);
    }

    [Fact]
    public void DefaultValueAccessor_ReturnsExpectedDefault()
    {
        var field = _provider.GetGroups()[0].Fields[0];
        Assert.NotNull(field.DefaultValueAccessor);
        Assert.Equal("90", field.DefaultValueAccessor!());
    }

    [Fact]
    public void Field_HasI18nKey()
    {
        var field = _provider.GetGroups()[0].Fields[0];
        Assert.NotNull(field.I18nKey);
    }
}
