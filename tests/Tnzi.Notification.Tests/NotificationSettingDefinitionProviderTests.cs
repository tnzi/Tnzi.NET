namespace Tnzi.Notification.Tests;

/// <summary>
/// NotificationSettingDefinitionProvider 结构测试 — 验证 provider 注册字段符合配置中心契约
/// </summary>
public class NotificationSettingDefinitionProviderTests
{
    private readonly NotificationSettingDefinitionProvider _provider = new();

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
        Assert.Equal("notification-general", group.Key);
    }

    [Fact]
    public void Group_HasExpectedModuleName()
    {
        var group = _provider.GetGroups()[0];
        Assert.Equal("Notification", group.ModuleName);
    }

    [Fact]
    public void Group_HasExpectedOrder()
    {
        var group = _provider.GetGroups()[0];
        Assert.Equal(400, group.Order);
    }

    [Fact]
    public void Group_HasThreeFields()
    {
        var group = _provider.GetGroups()[0];
        Assert.Equal(3, group.Fields.Count);
    }

    [Fact]
    public void Fields_HaveCorrectKeys()
    {
        var fields = _provider.GetGroups()[0].Fields;
        Assert.Contains(fields, f => f.Key == "Notification:SendTimeoutSeconds");
        Assert.Contains(fields, f => f.Key == "Notification:Retry:RetryDelaySeconds");
        Assert.Contains(fields, f => f.Key == "Notification:Retry:EnableExponentialBackoff");
    }

    [Fact]
    public void Fields_HaveCorrectTypes()
    {
        var fields = _provider.GetGroups()[0].Fields;
        var timeout = fields.First(f => f.Key == "Notification:SendTimeoutSeconds");
        var delay = fields.First(f => f.Key == "Notification:Retry:RetryDelaySeconds");
        var backoff = fields.First(f => f.Key == "Notification:Retry:EnableExponentialBackoff");

        Assert.Equal(SettingFieldType.Int, timeout.Type);
        Assert.Equal(SettingFieldType.Int, delay.Type);
        Assert.Equal(SettingFieldType.Boolean, backoff.Type);
    }

    [Fact]
    public void DefaultValueAccessors_ReturnExpectedDefaults()
    {
        var fields = _provider.GetGroups()[0].Fields;

        var timeout = fields.First(f => f.Key == "Notification:SendTimeoutSeconds");
        var delay = fields.First(f => f.Key == "Notification:Retry:RetryDelaySeconds");
        var backoff = fields.First(f => f.Key == "Notification:Retry:EnableExponentialBackoff");

        Assert.NotNull(timeout.DefaultValueAccessor);
        Assert.Equal("30", timeout.DefaultValueAccessor!());

        Assert.NotNull(delay.DefaultValueAccessor);
        Assert.Equal("60", delay.DefaultValueAccessor!());

        Assert.NotNull(backoff.DefaultValueAccessor);
        Assert.Equal("true", backoff.DefaultValueAccessor!());
    }

    [Fact]
    public void Fields_HaveI18nKeys()
    {
        var fields = _provider.GetGroups()[0].Fields;
        Assert.All(fields, f => Assert.NotNull(f.I18nKey));
    }
}
