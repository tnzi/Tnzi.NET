using Tnzi.System.Settings;

namespace Tnzi.AspNetCore.Tests.Settings;

/// <summary>
/// Verifies that the AspNetCore Options classes carry correct [RuntimeSetting] / [RuntimeSettingGroup]
/// attribute metadata after migrating away from the hand-written AspNetCoreSettingDefinitionProvider.
/// Tests use RuntimeSettingMetadataExtractor (same extractor as AttributeSettingDefinitionProvider)
/// and AttributeSettingDefinitionProvider.MergeByGroupKey to reproduce the three groups.
/// </summary>
public class AspNetCoreSettingDefinitionProviderTests
{
    private static IReadOnlyList<SettingDefinitionGroup> BuildGroups()
    {
        var rawGroups = new List<SettingDefinitionGroup>();
        foreach (var t in new[] { typeof(RequestTrackingOptions), typeof(ExceptionHandlingOptions), typeof(SecurityHeadersOptions), typeof(RateLimitOptions), typeof(ApiVersionOptions) })
        {
            var g = RuntimeSettingMetadataExtractor.Extract(t);
            if (g != null) rawGroups.Add(g);
        }
        return AttributeSettingDefinitionProvider.MergeByGroupKey(rawGroups);
    }

    [Fact]
    public void GetGroups_ReturnsFourGroups()
    {
        var groups = BuildGroups();
        Assert.Equal(4, groups.Count);
    }

    [Fact]
    public void GetGroups_AllGroupsHaveModuleNameWeb()
    {
        var groups = BuildGroups();
        Assert.All(groups, g => Assert.Equal("Web", g.ModuleName));
    }

    [Fact]
    public void GetGroups_KeysAreDistinct()
    {
        var groups = BuildGroups();
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
    public void ApiVersionGroup_HasCorrectMetadata()
    {
        var group = GetGroup("web-apiversion");
        Assert.Equal("Web", group.ModuleName);
        Assert.Equal(705, group.Order);
        Assert.Equal("mdi:tag-multiple-outline", group.Icon);
        Assert.Equal("admin.modules.system.settings.groups.webApiVersion", group.I18nKey);
    }

    [Fact]
    public void ApiVersionGroup_ExposesOnlyReportVersion()
    {
        // Only ReportVersion is hot-settable; readers/header names are client protocol contracts (KEEP-STATIC).
        var group = GetGroup("web-apiversion");
        Assert.All(group.Fields, f => Assert.StartsWith("AspNetCore:ApiVersion:", f.Key));
        Assert.Contains(group.Fields, f => f.Key == "AspNetCore:ApiVersion:ReportVersion");
        Assert.DoesNotContain(group.Fields, f => f.Key == "AspNetCore:ApiVersion:HeaderName");
        Assert.DoesNotContain(group.Fields, f => f.Key == "AspNetCore:ApiVersion:ReaderType");
    }

    [Fact]
    public void ObservabilityGroup_ExposesRequestTrackingLogLevelAsSelect()
    {
        var field = GetField("web-observability", "AspNetCore:RequestTracking:LogLevel");
        Assert.Equal(SettingFieldType.Select, field.Type);
        // Enum candidates auto-derived from LogLevel.
        Assert.NotNull(field.Options);
        Assert.Contains("Information", field.Options!);
        Assert.Contains("Warning", field.Options!);
    }

    [Fact]
    public void ObservabilityGroup_ExposesExceptionIncludeContextData()
    {
        var field = GetField("web-observability", "AspNetCore:ExceptionHandling:IncludeContextData");
        Assert.Equal(SettingFieldType.Boolean, field.Type);
    }

    [Fact]
    public void SecurityHeadersGroup_ExposesHstsAndPermissionsPolicyFields()
    {
        var group = GetGroup("web-security-headers");
        Assert.Contains(group.Fields, f => f.Key == "AspNetCore:SecurityHeaders:HstsIncludeSubDomains");
        Assert.Contains(group.Fields, f => f.Key == "AspNetCore:SecurityHeaders:HstsPreload");
        var permissionsPolicy = GetField("web-security-headers", "AspNetCore:SecurityHeaders:PermissionsPolicy");
        Assert.Equal(SettingFieldType.Text, permissionsPolicy.Type);
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
        // EnableMetrics 不收录：仅启动期门控 DI 注册，运行时无消费者（死字段）
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
    public void EnableRequestLogging_DefaultValue_IsTrue()
    {
        // RequestTrackingOptions.EnableRequestLogging default = true
        var field = GetField("web-observability", "AspNetCore:RequestTracking:EnableRequestLogging");
        Assert.NotNull(field.DefaultValueAccessor);
        Assert.Equal("True", field.DefaultValueAccessor!());
    }

    [Fact]
    public void RateLimitEnabled_DefaultValue_IsFalse()
    {
        // RateLimitOptions.Enabled default = false
        var field = GetField("web-ratelimit", "AspNetCore:RateLimit:Enabled");
        Assert.NotNull(field.DefaultValueAccessor);
        Assert.Equal("False", field.DefaultValueAccessor!());
    }

    [Fact]
    public void RateLimitDefaultLimit_DefaultValue_Is100()
    {
        var field = GetField("web-ratelimit", "AspNetCore:RateLimit:DefaultLimit");
        Assert.NotNull(field.DefaultValueAccessor);
        Assert.Equal("100", field.DefaultValueAccessor!());
    }

    [Fact]
    public void RateLimitDefaultWindowSeconds_DefaultValue_Is60()
    {
        var field = GetField("web-ratelimit", "AspNetCore:RateLimit:DefaultWindowSeconds");
        Assert.NotNull(field.DefaultValueAccessor);
        Assert.Equal("60", field.DefaultValueAccessor!());
    }

    [Fact]
    public void EnableSecurityHeaders_DefaultValue_IsFalse()
    {
        // SecurityHeadersOptions.EnableSecurityHeaders default = false
        var field = GetField("web-security-headers", "AspNetCore:SecurityHeaders:EnableSecurityHeaders");
        Assert.NotNull(field.DefaultValueAccessor);
        Assert.Equal("False", field.DefaultValueAccessor!());
    }

    [Fact]
    public void AllFields_HaveNonEmptyI18nKey()
    {
        var groups = BuildGroups();
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
        var groups = BuildGroups();
        foreach (var group in groups)
        {
            var keys = group.Fields.Select(f => f.Key).ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());
        }
    }

    [Fact]
    public void ContentSecurityPolicy_HasTypeText()
    {
        var field = GetField("web-security-headers", "AspNetCore:SecurityHeaders:ContentSecurityPolicy");
        Assert.Equal(SettingFieldType.Text, field.Type);
    }

    [Fact]
    public void NoConflictsAcrossAllGroups()
    {
        var groups = BuildGroups();
        // ValidateNoConflicts throws if any field key is duplicated across groups
        AttributeSettingDefinitionProvider.ValidateNoConflicts(groups);
    }

    private static SettingDefinitionGroup GetGroup(string key)
    {
        var group = BuildGroups().FirstOrDefault(g => g.Key == key);
        Assert.NotNull(group);
        return group;
    }

    private static SettingFieldDefinition GetField(string groupKey, string fieldKey)
    {
        var group = GetGroup(groupKey);
        var field = group.Fields.FirstOrDefault(f => f.Key == fieldKey);
        Assert.NotNull(field);
        return field;
    }
}
