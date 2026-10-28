using System.Globalization;
using Tnzi.Settings;

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

    [ConfigSection("Pat")]
    private sealed class PatternOptions
    {
        [RuntimeSetting(Pattern = @"https?://.+")] public string? Url { get; set; }
    }

    [ConfigSection("Pat")]
    private sealed class InvalidPatternOptions
    {
        [RuntimeSetting(Pattern = "([unclosed")] public string? Broken { get; set; }
    }

    [ConfigSection("Dur")]
    private sealed class DurationOptions
    {
        [RuntimeSetting] public TimeSpan Ttl { get; set; } = TimeSpan.FromMinutes(5);
        [RuntimeSetting] public TimeSpan? MaxIdle { get; set; } = TimeSpan.FromHours(1);
        [RuntimeSetting(Subsection = "Advanced")] public int Retries { get; set; } = 3;
    }

    [ConfigSection("Num")]
    private sealed class FractionalOptions
    {
        [RuntimeSetting] public double Ratio { get; set; } = 0.3;
        [RuntimeSetting] public decimal Tolerance { get; set; } = 0.05m;
        [RuntimeSetting] public float? Weight { get; set; } = 1.5f;
        [RuntimeSetting] public long Big { get; set; } = 1234567;
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

    [Fact]
    public void Carries_pattern_and_options_type()
    {
        var g = RuntimeSettingMetadataExtractor.Extract(typeof(PatternOptions))!;
        Assert.Equal([typeof(PatternOptions)], g.OptionsTypes);
        Assert.Equal(@"https?://.+", g.Fields.Single(f => f.Key == "Pat:Url").Pattern);
    }

    [Fact]
    public void Throws_on_invalid_pattern_regex()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => RuntimeSettingMetadataExtractor.Extract(typeof(InvalidPatternOptions)));
        Assert.Contains("Broken", ex.Message);
    }

    [Fact]
    public void Infers_duration_from_timespan_and_default_is_canonical_roundtrippable()
    {
        var g = RuntimeSettingMetadataExtractor.Extract(typeof(DurationOptions))!;

        var ttl = g.Fields.Single(f => f.Key == "Dur:Ttl");
        Assert.Equal(SettingFieldType.Duration, ttl.Type);
        // canonical TimeSpan.ToString() - 且必须 TimeSpan.TryParse 可逆（热更新写回不失败）。
        var defaultValue = ttl.DefaultValueAccessor!();
        Assert.Equal("00:05:00", defaultValue);
        Assert.True(TimeSpan.TryParse(defaultValue, out var parsed) && parsed == TimeSpan.FromMinutes(5));

        // Nullable TimeSpan? 同样推断为 Duration。
        Assert.Equal(SettingFieldType.Duration, g.Fields.Single(f => f.Key == "Dur:MaxIdle").Type);
    }

    [Fact]
    public void Carries_subsection_from_attribute()
    {
        var g = RuntimeSettingMetadataExtractor.Extract(typeof(DurationOptions))!;

        Assert.Equal("Advanced", g.Fields.Single(f => f.Key == "Dur:Retries").Subsection);
        // 未标 Subsection 的字段为 null。
        Assert.Null(g.Fields.Single(f => f.Key == "Dur:Ttl").Subsection);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public void Default_values_are_formatted_with_the_invariant_culture(string culture)
    {
        // 写回路径按不变文化解析；默认值若跟随当前区域性，逗号小数文化下会变成 "0,3"，
        // 设置中心把它解析成 NaN 显示为空。定义只构建一次并缓存，所以首个请求的区域性会影响所有人。
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            var g = RuntimeSettingMetadataExtractor.Extract(typeof(FractionalOptions))!;

            Assert.Equal("0.3", g.Fields.Single(f => f.Key == "Num:Ratio").DefaultValueAccessor!());
            Assert.Equal("0.05", g.Fields.Single(f => f.Key == "Num:Tolerance").DefaultValueAccessor!());
            Assert.Equal("1.5", g.Fields.Single(f => f.Key == "Num:Weight").DefaultValueAccessor!());
            Assert.Equal("1234567", g.Fields.Single(f => f.Key == "Num:Big").DefaultValueAccessor!());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
