namespace Tnzi.AspNetCore.Tests.Settings;

public class AspNetCoreSettingDefinitionProviderTests
{
    private readonly AspNetCoreSettingDefinitionProvider _provider = new();

    [Fact]
    public void GetGroups_ReturnsThreeGroups()
    {
        var groups = _provider.GetGroups();
        Assert.Equal(3, groups.Count);
    }

    [Fact]
    public void GetGroups_AllGroupsHaveModuleNameWeb()
    {
        var groups = _provider.GetGroups();
        Assert.All(groups, g => Assert.Equal("Web", g.ModuleName));
    }

    [Fact]
    public void GetGroups_KeysAreDistinct()
    {
        var groups = _provider.GetGroups();
        var keys = groups.Select(g => g.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void ObservabilityGroup_HasCorrectMetadata()
    {
        var group = GetGroup("web-observability");
        Assert.Equal("Web", group.ModuleName);
        Assert.Equal(700, group.Order);
        Assert.Equal("mdi:chart-timeline-variant", group.Icon);
        Assert.Equal("admin.modules.system.settings.groups.webObservability", group.I18nKey);
    }

    [Fact]
    public void SecurityHeadersGroup_HasCorrectMetadata()
    {
        var group = GetGroup("web-security-headers");
        Assert.Equal("Web", group.ModuleName);
        Assert.Equal(710, group.Order);
        Assert.Equal("mdi:shield-lock-outline", group.Icon);
        Assert.Equal("admin.modules.system.settings.groups.webSecurityHeaders", group.I18nKey);
    }

    [Fact]
    public void RateLimitGroup_HasCorrectMetadata()
    {
        var group = GetGroup("web-ratelimit");
        Assert.Equal("Web", group.ModuleName);
        Assert.Equal(720, group.Order);
        Assert.Equal("mdi:speedometer", group.Icon);
        Assert.Equal("admin.modules.system.settings.groups.webRatelimit", group.I18nKey);
    }

    [Fact]
    public void ObservabilityGroup_FieldKeysPrefixedWithAspNetCoreRequestTracking()
    {
        var group = GetGroup("web-observability");
        var trackingFields = group.Fields
            .Where(f => f.Key.StartsWith("AspNetCore:RequestTracking:"))
            .ToList();
        Assert.NotEmpty(trackingFields);
        Assert.Contains(trackingFields, f => f.Key == "AspNetCore:RequestTracking:EnableRequestLogging");
        Assert.Contains(trackingFields, f => f.Key == "AspNetCore:RequestTracking:LogRequestBody");
        Assert.Contains(trackingFields, f => f.Key == "AspNetCore:RequestTracking:LogResponseBody");
        Assert.Contains(trackingFields, f => f.Key == "AspNetCore:RequestTracking:MaxRequestBodyLength");
        Assert.Contains(trackingFields, f => f.Key == "AspNetCore:RequestTracking:MaxResponseBodyLength");
        Assert.Contains(trackingFields, f => f.Key == "AspNetCore:RequestTracking:SlowRequestThresholdMs");
    }

    [Fact]
    public void ObservabilityGroup_FieldKeysPrefixedWithAspNetCoreExceptionHandling()
    {
        var group = GetGroup("web-observability");
        var exFields = group.Fields
            .Where(f => f.Key.StartsWith("AspNetCore:ExceptionHandling:"))
            .ToList();
        Assert.NotEmpty(exFields);
        Assert.Contains(exFields, f => f.Key == "AspNetCore:ExceptionHandling:ShowDetailsInDevelopment");
        Assert.Contains(exFields, f => f.Key == "AspNetCore:ExceptionHandling:IncludeRequestId");
        // EnableMetrics 已移除：仅启动期门控 DI 注册，运行时无消费者（死字段）
        Assert.DoesNotContain(exFields, f => f.Key == "AspNetCore:ExceptionHandling:EnableMetrics");
    }

    [Fact]
    public void SecurityHeadersGroup_FieldKeysPrefixedWithAspNetCoreSecurityHeaders()
    {
        var group = GetGroup("web-security-headers");
        Assert.All(group.Fields, f => Assert.StartsWith("AspNetCore:SecurityHeaders:", f.Key));
    }

    [Fact]
    public void RateLimitGroup_FieldKeysPrefixedWithAspNetCoreRateLimit()
    {
        var group = GetGroup("web-ratelimit");
        Assert.All(group.Fields, f => Assert.StartsWith("AspNetCore:RateLimit:", f.Key));
    }

    [Fact]
    public void RateLimitGroup_ContainsExpectedFields()
    {
        var group = GetGroup("web-ratelimit");
        Assert.Contains(group.Fields, f => f.Key == "AspNetCore:RateLimit:Enabled");
        Assert.Contains(group.Fields, f => f.Key == "AspNetCore:RateLimit:DefaultLimit");
        Assert.Contains(group.Fields, f => f.Key == "AspNetCore:RateLimit:DefaultWindowSeconds");
        Assert.Contains(group.Fields, f => f.Key == "AspNetCore:RateLimit:AllowOnFailure");
    }

    [Fact]
    public void EnableRequestLogging_DefaultValueAccessor_ReturnsFalseString()
    {
        // RequestTrackingOptions.EnableRequestLogging default = true
        var field = GetField("web-observability", "AspNetCore:RequestTracking:EnableRequestLogging");
        Assert.NotNull(field.DefaultValueAccessor);
        Assert.Equal("true", field.DefaultValueAccessor!());
    }

    [Fact]
    public void RateLimitEnabled_DefaultValueAccessor_ReturnsFalseString()
    {
        // RateLimitOptions.Enabled default = false
        var field = GetField("web-ratelimit", "AspNetCore:RateLimit:Enabled");
        Assert.NotNull(field.DefaultValueAccessor);
        Assert.Equal("false", field.DefaultValueAccessor!());
    }

    [Fact]
    public void RateLimitDefaultLimit_DefaultValueAccessor_Returns100()
    {
        var field = GetField("web-ratelimit", "AspNetCore:RateLimit:DefaultLimit");
        Assert.NotNull(field.DefaultValueAccessor);
        Assert.Equal("100", field.DefaultValueAccessor!());
    }

    [Fact]
    public void RateLimitDefaultWindowSeconds_DefaultValueAccessor_Returns60()
    {
        var field = GetField("web-ratelimit", "AspNetCore:RateLimit:DefaultWindowSeconds");
        Assert.NotNull(field.DefaultValueAccessor);
        Assert.Equal("60", field.DefaultValueAccessor!());
    }

    [Fact]
    public void EnableSecurityHeaders_DefaultValueAccessor_ReturnsFalseString()
    {
        // SecurityHeadersOptions.EnableSecurityHeaders default = false
        var field = GetField("web-security-headers", "AspNetCore:SecurityHeaders:EnableSecurityHeaders");
        Assert.NotNull(field.DefaultValueAccessor);
        Assert.Equal("false", field.DefaultValueAccessor!());
    }

    [Fact]
    public void AllFields_HaveNonEmptyI18nKey()
    {
        var groups = _provider.GetGroups();
        foreach (var group in groups)
        {
            Assert.All(group.Fields, f =>
            {
                Assert.NotNull(f.I18nKey);
                Assert.StartsWith("admin.modules.system.settings.fields.", f.I18nKey);
            });
        }
    }

    [Fact]
    public void AllFields_HaveDistinctKeysWithinEachGroup()
    {
        var groups = _provider.GetGroups();
        foreach (var group in groups)
        {
            var keys = group.Fields.Select(f => f.Key).ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());
        }
    }

    [Fact]
    public void BooleanFields_DefaultValueAccessors_ReturnLowercaseString()
    {
        var groups = _provider.GetGroups();
        var boolFields = groups.SelectMany(g => g.Fields)
            .Where(f => f.Type == SettingFieldType.Boolean && f.DefaultValueAccessor != null)
            .ToList();

        Assert.NotEmpty(boolFields);
        foreach (var field in boolFields)
        {
            var value = field.DefaultValueAccessor!();
            if (value != null)
            {
                Assert.True(value == "true" || value == "false",
                    $"Field {field.Key} default accessor returned '{value}', expected 'true' or 'false'");
            }
        }
    }

    private SettingDefinitionGroup GetGroup(string key)
    {
        var group = _provider.GetGroups().FirstOrDefault(g => g.Key == key);
        Assert.NotNull(group);
        return group;
    }

    private SettingFieldDefinition GetField(string groupKey, string fieldKey)
    {
        var group = GetGroup(groupKey);
        var field = group.Fields.FirstOrDefault(f => f.Key == fieldKey);
        Assert.NotNull(field);
        return field;
    }
}
