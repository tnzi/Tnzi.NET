
namespace Tnzi.Notification.Tests;

/// <summary>
/// Notification 配置中心属性驱动定义测试 - 验证 NotificationOptions + RetryOptions 的
/// [RuntimeSetting] 注解经 RuntimeSettingMetadataExtractor 扫描合并后符合配置中心契约。
/// </summary>
public class NotificationSettingDefinitionProviderTests
{
    private readonly SettingDefinitionGroup _group;

    public NotificationSettingDefinitionProviderTests()
    {
        // Mirrors AttributeSettingDefinitionProvider: extract per-type, then merge by group key.
        var raw = new List<SettingDefinitionGroup>();
        foreach (var type in new[] { typeof(NotificationOptions), typeof(RetryOptions) })
        {
            var g = RuntimeSettingMetadataExtractor.Extract(type);
            if (g != null) raw.Add(g);
        }

        // Inline merge (same logic as AttributeSettingDefinitionProvider.MergeByGroupKey).
        var merged = raw
            .GroupBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(cluster =>
            {
                var first = cluster.First();
                if (cluster.Count() == 1) return first;
                return new SettingDefinitionGroup
                {
                    Key = first.Key,
                    ModuleName = first.ModuleName,
                    DisplayName = first.DisplayName,
                    I18nKey = first.I18nKey,
                    Icon = first.Icon,
                    Order = cluster.Min(c => c.Order),
                    Fields = cluster.SelectMany(c => c.Fields).ToList(),
                };
            })
            .ToList();

        _group = merged.Single();
    }

    [Fact]
    public void GetGroups_ReturnsOneGroup()
    {
        _group.ShouldNotBeNull();
    }

    [Fact]
    public void Group_HasExpectedKey()
    {
        _group.Key.ShouldBe("notification-general");
    }

    [Fact]
    public void Group_HasExpectedModuleName()
    {
        _group.ModuleName.ShouldBe("Notification");
    }

    [Fact]
    public void Group_HasExpectedOrder()
    {
        _group.Order.ShouldBe(400);
    }

    [Fact]
    public void Group_HasThreeFields()
    {
        _group.Fields.Count.ShouldBe(3);
    }

    [Fact]
    public void Fields_HaveCorrectKeys()
    {
        _group.Fields.ShouldContain(f => f.Key == "Notification:SendTimeoutSeconds");
        _group.Fields.ShouldContain(f => f.Key == "Notification:Retry:RetryDelaySeconds");
        _group.Fields.ShouldContain(f => f.Key == "Notification:Retry:EnableExponentialBackoff");
    }

    [Fact]
    public void Fields_HaveCorrectTypes()
    {
        var timeout = _group.Fields.Single(f => f.Key == "Notification:SendTimeoutSeconds");
        var delay = _group.Fields.Single(f => f.Key == "Notification:Retry:RetryDelaySeconds");
        var backoff = _group.Fields.Single(f => f.Key == "Notification:Retry:EnableExponentialBackoff");

        timeout.Type.ShouldBe(SettingFieldType.Int);
        delay.Type.ShouldBe(SettingFieldType.Int);
        backoff.Type.ShouldBe(SettingFieldType.Boolean);
    }

    [Fact]
    public void DefaultValueAccessors_ReturnExpectedDefaults()
    {
        var timeout = _group.Fields.Single(f => f.Key == "Notification:SendTimeoutSeconds");
        var delay = _group.Fields.Single(f => f.Key == "Notification:Retry:RetryDelaySeconds");
        var backoff = _group.Fields.Single(f => f.Key == "Notification:Retry:EnableExponentialBackoff");

        timeout.DefaultValueAccessor.ShouldNotBeNull();
        timeout.DefaultValueAccessor!().ShouldBe("30");

        delay.DefaultValueAccessor.ShouldNotBeNull();
        delay.DefaultValueAccessor!().ShouldBe("60");

        backoff.DefaultValueAccessor.ShouldNotBeNull();
        backoff.DefaultValueAccessor!().ShouldBe("True");
    }

    [Fact]
    public void Fields_HaveI18nKeys()
    {
        _group.Fields.ShouldAllBe(f => f.I18nKey != null);
    }
}
