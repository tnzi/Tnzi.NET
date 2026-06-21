namespace Tnzi.Settings;

public class RuntimeSettingAttributeTests
{
    [ConfigSection("Demo")]
    [RuntimeSettingGroup(Key = "demo", Module = "Demo", DisplayName = "Demo", Order = 2)]
    private sealed class DemoOptions
    {
        [RuntimeSetting(Label = "Name", Type = SettingFieldType.String)]
        public string Name { get; set; } = "x";
    }

    [Fact]
    public void Attributes_are_readable_via_reflection()
    {
        var cs = typeof(DemoOptions).GetCustomAttribute<ConfigSectionAttribute>();
        Assert.Equal("Demo", cs!.Section);

        var g = typeof(DemoOptions).GetCustomAttribute<RuntimeSettingGroupAttribute>();
        Assert.Equal("demo", g!.Key);
        Assert.Equal(2, g.Order);

        var prop = typeof(DemoOptions).GetProperty(nameof(DemoOptions.Name))!;
        var rs = prop.GetCustomAttribute<RuntimeSettingAttribute>();
        Assert.Equal("Name", rs!.Label);
        Assert.Equal(SettingFieldType.String, rs.Type);
        Assert.Equal(SettingFieldType.Auto, new RuntimeSettingAttribute().Type); // 默认 Type=Auto
    }
}
