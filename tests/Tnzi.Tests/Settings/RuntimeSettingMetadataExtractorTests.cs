using System.Linq;
using Tnzi.Settings;
using Xunit;

public class RuntimeSettingMetadataExtractorTests
{
    private enum Mode { A, B, C }

    [ConfigSection("Demo")]
    [RuntimeSettingGroup(Key = "demo-grp", Module = "Demo", DisplayName = "Demo", Order = 3)]
    private sealed class DemoOptions
    {
        [RuntimeSetting(Label = "Site")] public string Site { get; set; } = "hello";
        [RuntimeSetting] public bool Flag { get; set; } = true;
        [RuntimeSetting(Min = 1, Max = 10)] public int Count { get; set; } = 5;
        [RuntimeSetting] public Mode Mode { get; set; } = Mode.B;
        [RuntimeSetting(Type = SettingFieldType.Password)] public string? Secret { get; set; }
        public string NotASetting { get; set; } = "ignored";
    }

    private sealed class PlainOptions { public string X { get; set; } = ""; }

    private sealed class Nested { public string A { get; set; } = ""; }

    [ConfigSection("Bad")]
    private sealed class NonScalarObjectOptions
    {
        [RuntimeSetting] public Nested Child { get; set; } = new();
    }

    [ConfigSection("Bad")]
    private sealed class NonScalarCollectionOptions
    {
        [RuntimeSetting] public List<string> Items { get; set; } = [];
    }

    [Fact]
    public void Returns_null_when_no_runtime_settings()
        => Assert.Null(RuntimeSettingMetadataExtractor.Extract(typeof(PlainOptions)));

    [Fact]
    public void Throws_when_runtime_setting_on_nested_object()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => RuntimeSettingMetadataExtractor.Extract(typeof(NonScalarObjectOptions)));
        Assert.Contains("Child", ex.Message);
    }

    [Fact]
    public void Throws_when_runtime_setting_on_collection()
        => Assert.Throws<InvalidOperationException>(
            () => RuntimeSettingMetadataExtractor.Extract(typeof(NonScalarCollectionOptions)));

    [Fact]
    public void Derives_group_and_fields()
    {
        var g = RuntimeSettingMetadataExtractor.Extract(typeof(DemoOptions))!;
        Assert.Equal("demo-grp", g.Key);
        Assert.Equal("Demo", g.ModuleName);
        Assert.Equal(3, g.Order);
        Assert.Equal(5, g.Fields.Count); // NotASetting 被排除

        var site = g.Fields.Single(f => f.Key == "Demo:Site");
        Assert.Equal("Site", site.Label);
        Assert.Equal(SettingFieldType.String, site.Type);
        Assert.Equal("hello", site.DefaultValueAccessor!());

        Assert.Equal(SettingFieldType.Boolean, g.Fields.Single(f => f.Key == "Demo:Flag").Type);

        var count = g.Fields.Single(f => f.Key == "Demo:Count");
        Assert.Equal(SettingFieldType.Int, count.Type);
        Assert.Equal(1, count.Min);
        Assert.Equal(10, count.Max);

        var mode = g.Fields.Single(f => f.Key == "Demo:Mode");
        Assert.Equal(SettingFieldType.Select, mode.Type);
        Assert.Equal(new[] { "A", "B", "C" }, mode.Options);
        Assert.Equal("B", mode.DefaultValueAccessor!());

        var secret = g.Fields.Single(f => f.Key == "Demo:Secret");
        Assert.True(secret.IsEncrypted); // 派生自 Type==Password
    }
}
