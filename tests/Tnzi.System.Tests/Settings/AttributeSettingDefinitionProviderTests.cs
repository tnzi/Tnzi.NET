namespace Tnzi.System.Tests.Settings;

public class AttributeSettingDefinitionProviderTests
{
    [Fact]
    public void ApplicationOptions_yields_system_general_group()
    {
        var g = RuntimeSettingMetadataExtractor.Extract(typeof(ApplicationOptions))!;
        Assert.Equal("system-general", g.Key);
        Assert.Equal("System", g.ModuleName);
        Assert.Contains(g.Fields, f => f.Key == "System:SiteName" && f.Label == "Site Name");
        Assert.Contains(g.Fields, f => f.Key == "System:AppName");
        // 默认值经反射 new ApplicationOptions().SiteName
        Assert.Equal("Tnzi.NET", g.Fields.Single(f => f.Key == "System:SiteName").DefaultValueAccessor!());
    }

    [Fact]
    public void ApplicationOptions_yields_12_fields()
    {
        var g = RuntimeSettingMetadataExtractor.Extract(typeof(ApplicationOptions))!;
        Assert.Equal(12, g.Fields.Count);
    }

    [Fact]
    public void ApplicationOptions_group_has_correct_icon_and_i18n()
    {
        var g = RuntimeSettingMetadataExtractor.Extract(typeof(ApplicationOptions))!;
        Assert.Equal("mdi:web", g.Icon);
        Assert.Equal("admin.modules.system.settings.groups.systemGeneral", g.I18nKey);
    }

    [Fact]
    public void Provider_with_no_application_still_returns_groups()
    {
        // Without ITnziApplication, falls back to AppDomain.CurrentDomain.GetAssemblies()
        var provider = new AttributeSettingDefinitionProvider();
        var groups = provider.GetGroups();
        // The test assembly has loaded Tnzi.System which contains ApplicationOptions - expect system-general
        Assert.Contains(groups, g => g.Key == "system-general");
    }

    [Fact]
    public void Provider_caches_result_on_second_call()
    {
        var provider = new AttributeSettingDefinitionProvider();
        var first = provider.GetGroups();
        var second = provider.GetGroups();
        Assert.Same(first, second);
    }

    // --- MergeByGroupKey ---

    [Fact]
    public void MergeByGroupKey_single_group_is_pass_through()
    {
        var field = new SettingFieldDefinition { Key = "Sec:Prop", Label = "Prop" };
        var input = new List<SettingDefinitionGroup>
        {
            new() { Key = "solo", ModuleName = "M", DisplayName = "Solo", Order = 3, Fields = [field] },
        };
        var result = AttributeSettingDefinitionProvider.MergeByGroupKey(input);
        result.Count.ShouldBe(1);
        result[0].ShouldBeSameAs(input[0]);
    }

    [Fact]
    public void MergeByGroupKey_two_same_key_groups_merge_into_one()
    {
        var field1 = new SettingFieldDefinition { Key = "A:Prop1", Label = "P1" };
        var field2 = new SettingFieldDefinition { Key = "B:Prop2", Label = "P2" };
        var rawGroups = new List<SettingDefinitionGroup>
        {
            new() { Key = "shared", ModuleName = "Primary", DisplayName = "Primary", Order = 0,
                    Icon = "mdi:star", I18nKey = "i18n.primary", Description = "Desc", Fields = [field1] },
            new() { Key = "shared", ModuleName = "Secondary", DisplayName = "Secondary", Order = 5,
                    Fields = [field2] },
        };
        var result = AttributeSettingDefinitionProvider.MergeByGroupKey(rawGroups);

        result.Count.ShouldBe(1);
        var merged = result[0];
        merged.Key.ShouldBe("shared");
        merged.DisplayName.ShouldBe("Primary");   // primary (Order=0) wins
        merged.ModuleName.ShouldBe("Primary");
        merged.Order.ShouldBe(0);                 // MIN of 0 and 5
        merged.Icon.ShouldBe("mdi:star");
        merged.I18nKey.ShouldBe("i18n.primary");
        merged.Description.ShouldBe("Desc");
        merged.Fields.Count.ShouldBe(2);
        merged.Fields[0].Key.ShouldBe("A:Prop1"); // primary contributor fields first
        merged.Fields[1].Key.ShouldBe("B:Prop2");
    }

    [Fact]
    public void ApplicationOptions_group_is_built_in()
    {
        // ApplicationOptions lives in the Tnzi core assembly → framework built-in,
        // so the admin "Built-in menus" toggle hides it. Consumer app Options
        // (non-Tnzi assembly) resolve to IsBuiltIn=false (see Acme SmokeTests).
        var g = RuntimeSettingMetadataExtractor.Extract(typeof(ApplicationOptions))!;
        Assert.True(g.IsBuiltIn);
    }

    [Fact]
    public void MergeByGroupKey_mixed_builtin_and_consumer_is_not_builtin()
    {
        // A single consumer contribution keeps the merged group visible under the
        // toggle (IsBuiltIn = All contributors built-in).
        var rawGroups = new List<SettingDefinitionGroup>
        {
            new() { Key = "shared", ModuleName = "M", DisplayName = "Fw", Order = 0, IsBuiltIn = true,
                    Fields = [new SettingFieldDefinition { Key = "A:P1", Label = "P1" }] },
            new() { Key = "shared", ModuleName = "M", DisplayName = "App", Order = 1, IsBuiltIn = false,
                    Fields = [new SettingFieldDefinition { Key = "B:P2", Label = "P2" }] },
        };
        var merged = AttributeSettingDefinitionProvider.MergeByGroupKey(rawGroups);
        merged.Count.ShouldBe(1);
        merged[0].IsBuiltIn.ShouldBeFalse();
    }

    [Fact]
    public void MergeByGroupKey_carries_all_contributor_options_types()
    {
        // 回归：validator 预检要对合并组的每个贡献 Options 类型分别绑定验证 -
        // 合并时丢 OptionsTypes 会让整组静默跳过预检。
        var rawGroups = new List<SettingDefinitionGroup>
        {
            new() { Key = "shared", ModuleName = "A", DisplayName = "A", Order = 0,
                    OptionsTypes = [typeof(string)],
                    Fields = [new SettingFieldDefinition { Key = "A:P", Label = "P" }] },
            new() { Key = "shared", ModuleName = "B", DisplayName = "B", Order = 5,
                    OptionsTypes = [typeof(int)],
                    Fields = [new SettingFieldDefinition { Key = "B:P", Label = "P" }] },
        };

        var merged = AttributeSettingDefinitionProvider.MergeByGroupKey(rawGroups)[0];

        merged.OptionsTypes.ShouldBe([typeof(string), typeof(int)]);
    }

    [Fact]
    public void MergeByGroupKey_secondary_fills_null_metadata_from_primary()
    {
        var field1 = new SettingFieldDefinition { Key = "A:X", Label = "X" };
        var field2 = new SettingFieldDefinition { Key = "B:Y", Label = "Y" };
        var rawGroups = new List<SettingDefinitionGroup>
        {
            // Order=0, but no Icon or I18nKey
            new() { Key = "shared", ModuleName = "Primary", DisplayName = "Primary", Order = 0,
                    Fields = [field1] },
            // Order=1, has Icon and I18nKey
            new() { Key = "shared", ModuleName = "Secondary", DisplayName = "Secondary", Order = 1,
                    Icon = "mdi:cog", I18nKey = "i18n.sec", Fields = [field2] },
        };
        var result = AttributeSettingDefinitionProvider.MergeByGroupKey(rawGroups);
        var merged = result[0];
        // Primary wins DisplayName; secondary fills Icon/I18nKey because primary had null
        merged.DisplayName.ShouldBe("Primary");
        merged.Icon.ShouldBe("mdi:cog");
        merged.I18nKey.ShouldBe("i18n.sec");
    }

    [Fact]
    public void MergeByGroupKey_key_comparison_is_case_insensitive()
    {
        var f1 = new SettingFieldDefinition { Key = "A:P", Label = "P" };
        var f2 = new SettingFieldDefinition { Key = "B:Q", Label = "Q" };
        var rawGroups = new List<SettingDefinitionGroup>
        {
            new() { Key = "My-Group", ModuleName = "M1", DisplayName = "D1", Order = 0, Fields = [f1] },
            new() { Key = "my-group", ModuleName = "M2", DisplayName = "D2", Order = 1, Fields = [f2] },
        };
        var result = AttributeSettingDefinitionProvider.MergeByGroupKey(rawGroups);
        result.Count.ShouldBe(1);
        result[0].Fields.Count.ShouldBe(2);
    }

    // --- ValidateNoConflicts ---

    [Fact]
    public void ValidateNoConflicts_duplicate_group_key_does_not_throw()
    {
        // After MergeByGroupKey, group keys are unique by construction, but ValidateNoConflicts
        // itself no longer checks group key uniqueness - it only checks field key uniqueness.
        var field1 = new SettingFieldDefinition { Key = "A:Prop", Label = "P1" };
        var field2 = new SettingFieldDefinition { Key = "B:Prop", Label = "P2" };
        var groups = new List<SettingDefinitionGroup>
        {
            new() { Key = "my-group", ModuleName = "A", DisplayName = "A", Fields = [field1] },
            new() { Key = "my-group", ModuleName = "B", DisplayName = "B", Fields = [field2] },
        };
        // Should not throw - duplicate group keys are allowed here; MergeByGroupKey handles them.
        Should.NotThrow(() => AttributeSettingDefinitionProvider.ValidateNoConflicts(groups));
    }

    [Fact]
    public void ValidateNoConflicts_duplicate_field_key_across_groups_throws()
    {
        var field1 = new SettingFieldDefinition { Key = "Shared:Prop", Label = "Prop1" };
        var field2 = new SettingFieldDefinition { Key = "Shared:Prop", Label = "Prop2" };
        var groups = new List<SettingDefinitionGroup>
        {
            new() { Key = "group-a", ModuleName = "A", DisplayName = "A", Fields = [field1] },
            new() { Key = "group-b", ModuleName = "B", DisplayName = "B", Fields = [field2] },
        };
        Should.Throw<InvalidOperationException>(() => AttributeSettingDefinitionProvider.ValidateNoConflicts(groups))
            .Message.ShouldContain("Shared:Prop");
    }

    [Fact]
    public void ValidateNoConflicts_distinct_keys_does_not_throw()
    {
        var groups = new List<SettingDefinitionGroup>
        {
            new() { Key = "group-a", ModuleName = "A", DisplayName = "A", Fields =
                [new SettingFieldDefinition { Key = "A:Prop1", Label = "Prop1" }] },
            new() { Key = "group-b", ModuleName = "B", DisplayName = "B", Fields =
                [new SettingFieldDefinition { Key = "B:Prop2", Label = "Prop2" }] },
        };
        Should.NotThrow(() => AttributeSettingDefinitionProvider.ValidateNoConflicts(groups));
    }

    [Fact]
    public void ApplicationOptions_all_12_fields_have_correct_i18n_keys()
    {
        var g = RuntimeSettingMetadataExtractor.Extract(typeof(ApplicationOptions))!;
        var fieldByKey = g.Fields.ToDictionary(f => f.Key);

        static void AssertI18n(Dictionary<string, SettingFieldDefinition> map, string key, string expectedI18n)
            => Assert.Equal(expectedI18n, map[key].I18nKey);

        const string prefix = "admin.modules.system.settings.fields.";
        AssertI18n(fieldByKey, "System:AppName",     $"{prefix}appName");
        AssertI18n(fieldByKey, "System:SiteName",    $"{prefix}siteName");
        AssertI18n(fieldByKey, "System:FrontendUrl", $"{prefix}frontendUrl");
        AssertI18n(fieldByKey, "System:ApiBaseUrl",  $"{prefix}apiBaseUrl");
        AssertI18n(fieldByKey, "System:Email",       $"{prefix}email");
        AssertI18n(fieldByKey, "System:Phone",       $"{prefix}phone");
        AssertI18n(fieldByKey, "System:CompanyName", $"{prefix}companyName");
        AssertI18n(fieldByKey, "System:Address",     $"{prefix}address");
        AssertI18n(fieldByKey, "System:WebsiteUrl",  $"{prefix}websiteUrl");
        AssertI18n(fieldByKey, "System:LogoUrl",     $"{prefix}logoUrl");
        AssertI18n(fieldByKey, "System:Copyright",   $"{prefix}copyright");
        AssertI18n(fieldByKey, "System:IcpNumber",   $"{prefix}icpNumber");
    }
}
