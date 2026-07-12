namespace Tnzi.System.Tests.Settings;

using Tnzi.Security.Authorization;
using Tnzi.System.Settings;

public class SettingsPermissionDefinitionProviderTests
{
    private sealed class FakeProvider(params SettingDefinitionGroup[] groups) : ISettingDefinitionProvider
    {
        public IReadOnlyList<SettingDefinitionGroup> GetGroups() => groups;
    }

    private static SettingDefinitionGroup Group(string key, string module, string? permGroup = null) => new()
    {
        Key = key,
        ModuleName = module,
        DisplayName = module,
        PermissionGroup = permGroup,
        Fields = [new SettingFieldDefinition { Key = $"{module}:X", Label = "X" }],
    };

    private static PermissionDefinitionContext Define(params ISettingDefinitionProvider[] providers)
    {
        var context = new PermissionDefinitionContext();
        new SettingsPermissionDefinitionProvider(providers).Define(context);
        return context;
    }

    [Fact]
    public void Emits_view_and_update_code_per_group_parented_on_the_module_group()
    {
        var context = Define(new FakeProvider(
            Group("chat-general", "Chat"),
            Group("ai-budget", "AI")));

        // Two codes per group.
        context.Permissions.Count.ShouldBe(4);

        var chatView = context.Permissions["chat.settings.general.view"];
        chatView.ParentName.ShouldBe("chat");
        chatView.Category.ShouldBe(PermissionCategory.Technical);
        context.Permissions.ContainsKey("chat.settings.general.update").ShouldBeTrue();
        context.Permissions["ai.settings.budget.view"].ParentName.ShouldBe("ai");
        context.Permissions.ContainsKey("ai.settings.budget.update").ShouldBeTrue();
    }

    [Fact]
    public void Web_group_with_explicit_permission_group_parents_on_system()
    {
        var context = Define(new FakeProvider(Group("web-observability", "Web", permGroup: "system")));

        var view = context.Permissions["system.settings.webObservability.view"];
        view.ParentName.ShouldBe("system");
    }

    [Fact]
    public void Duplicate_group_keys_across_providers_are_deduped()
    {
        var context = Define(
            new FakeProvider(Group("chat-general", "Chat")),
            new FakeProvider(Group("chat-general", "Chat")));

        context.Permissions.Count.ShouldBe(2);
    }
}
