namespace Tnzi.Tests.Settings;

using Tnzi.Settings;
using Xunit;

public class SettingsPermissionNamingTests
{
    private static SettingDefinitionGroup Group(
        string key, string moduleName, string? permGroup = null, string? permSlug = null)
        => new()
        {
            Key = key,
            ModuleName = moduleName,
            DisplayName = moduleName,
            PermissionGroup = permGroup,
            PermissionSlug = permSlug,
            Fields = [],
        };

    [Theory]
    [InlineData("chat-general", "Chat", "chat", "general", "chat.settings.general.view")]
    [InlineData("ai-budget", "AI", "ai", "budget", "ai.settings.budget.view")]
    [InlineData("ai-conversation", "AI", "ai", "conversation", "ai.settings.conversation.view")]
    [InlineData("ai-summarization", "AI", "ai", "summarization", "ai.settings.summarization.view")]
    [InlineData("system-general", "System", "system", "general", "system.settings.general.view")]
    [InlineData("identity-security", "Identity", "identity", "security", "identity.settings.security.view")]
    [InlineData("identity-registration", "Identity", "identity", "registration", "identity.settings.registration.view")]
    [InlineData("storage-upload", "Storage", "storage", "upload", "storage.settings.upload.view")]
    [InlineData("notification-general", "Notification", "notification", "general", "notification.settings.general.view")]
    [InlineData("payment-general", "Payment", "payment", "general", "payment.settings.general.view")]
    [InlineData("audit-retention", "Audit", "audit", "retention", "audit.settings.retention.view")]
    public void Derives_group_slug_and_codes_from_module_name(
        string key, string module, string expectedGroup, string expectedSlug, string expectedView)
    {
        var g = Group(key, module);

        Assert.Equal(expectedGroup, SettingsPermissionNaming.GroupName(g));
        Assert.Equal(expectedSlug, SettingsPermissionNaming.Slug(g));
        Assert.Equal(expectedView, SettingsPermissionNaming.ViewCode(g));
        Assert.Equal(expectedView.Replace(".view", ".update"), SettingsPermissionNaming.UpdateCode(g));
    }

    [Theory]
    // Web/AspNetCore groups explicitly map to the "system" permission group; the
    // slug keeps the "web" prefix (camelCased) so the code reads as a system
    // surface without colliding with system-general.
    [InlineData("web-observability", "system.settings.webObservability.view")]
    [InlineData("web-security-headers", "system.settings.webSecurityHeaders.view")]
    [InlineData("web-ratelimit", "system.settings.webRatelimit.view")]
    public void Web_groups_map_to_system_permission_group(string key, string expectedView)
    {
        var g = Group(key, "Web", permGroup: "system");

        Assert.Equal("system", SettingsPermissionNaming.GroupName(g));
        Assert.Equal(expectedView, SettingsPermissionNaming.ViewCode(g));
    }

    [Fact]
    public void Explicit_permission_group_and_slug_win()
    {
        var g = Group("weird-key", "Anything", permGroup: "finance", permSlug: "billing");

        Assert.Equal("finance.settings.billing.view", SettingsPermissionNaming.ViewCode(g));
        Assert.Equal("finance.settings.billing.update", SettingsPermissionNaming.UpdateCode(g));
    }

    [Fact]
    public void Module_name_is_normalized_to_lowercase_alphanumeric()
    {
        Assert.Equal("ai", SettingsPermissionNaming.GroupName(Group("x-general", "AI")));
        Assert.Equal("webapp", SettingsPermissionNaming.GroupName(Group("x-general", "Web App")));
    }

    [Fact]
    public void Key_without_module_prefix_is_camel_cased_whole()
    {
        // Key first segment differs from the group -> keep every segment.
        Assert.Equal("fooBar", SettingsPermissionNaming.Slug(Group("foo-bar", "chat")));
    }

    [Theory]
    [InlineData("chat.settings.general.view", true)]
    [InlineData("chat.settings.general.update", true)]
    [InlineData("system.settings.webObservability.view", true)]
    [InlineData("chat.session.view", false)]
    [InlineData("system.parameter.view", false)]
    [InlineData("chat.settings.general.delete", false)]
    [InlineData("settings.view", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSettingsPermissionCode_matches_only_settings_view_update(string? code, bool expected)
    {
        Assert.Equal(expected, SettingsPermissionNaming.IsSettingsPermissionCode(code));
    }
}
