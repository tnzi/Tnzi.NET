namespace Tnzi.Authorization.Tests.Permissions;

/// <summary>
/// Locks the framework permission catalogue (<see cref="FrameworkPermissions"/>)
/// against drift. These are the codes the admin shell's routes reference via
/// <c>meta.permission</c>; if a front-end route adds a code without a matching
/// backend declaration, a super-admin would stop seeing that menu and a normal
/// user could never be granted it. The count + key-code assertions make that
/// kind of regression a failing test instead of a silent gap.
/// </summary>
public class FrameworkPermissionsTests
{
    private static PermissionDefinitionContext BuildContext()
    {
        var context = new PermissionDefinitionContext();
        new FrameworkPermissions().Define(context);
        return context;
    }

    [Fact]
    public void Define_declares_expected_codes_without_duplicates()
    {
        var context = BuildContext();
        var codes = context.Permissions.Values.Select(p => p.Name).ToList();

        // No duplicate permission codes.
        codes.Count.ShouldBe(codes.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // Locked counts — bump these deliberately when adding/removing codes so
        // an accidental drift (front-end route added without a backend code, or
        // vice versa) shows up as a failing test.
        codes.Count.ShouldBe(80);
        context.Groups.Count.ShouldBe(12);
    }

    [Fact]
    public void Define_declares_the_admin_gate_as_business()
    {
        var context = BuildContext();

        // The ApiAdminControllerBase outer gate must exist in the catalogue
        // (otherwise it is a ghost permission only super-admins can pass)
        // and must be Business so business admins can enter the admin area.
        context.Permissions.ContainsKey("Admin.Manage").ShouldBeTrue();
        context.Permissions["Admin.Manage"].Category.ShouldBe(PermissionCategory.Business);
    }

    [Theory]
    // Technical: system/ops surfaces business admins must not reach.
    [InlineData("tenant.view", PermissionCategory.Technical)]
    [InlineData("session.view", PermissionCategory.Technical)]
    // Whole Authorization module is Technical — managing roles/permissions is a
    // security concern, and every page needs the function-module catalogue
    // (authorization.functionModule.view) to work, so the tier must be uniform.
    [InlineData("authorization.view", PermissionCategory.Technical)]
    [InlineData("authorization.functionModule.view", PermissionCategory.Technical)]
    [InlineData("authorization.permission.view", PermissionCategory.Technical)]
    [InlineData("authorization.roleFunction.view", PermissionCategory.Technical)]
    [InlineData("authorization.entityRole.view", PermissionCategory.Technical)]
    [InlineData("feature.view", PermissionCategory.Technical)]
    [InlineData("system.parameter.view", PermissionCategory.Technical)]
    [InlineData("system.accessLog.view", PermissionCategory.Technical)]
    [InlineData("system.scheduledJob.view", PermissionCategory.Technical)]
    [InlineData("system.diagnostics.view", PermissionCategory.Technical)]
    [InlineData("system.health.view", PermissionCategory.Technical)]
    [InlineData("system.localization.view", PermissionCategory.Technical)]
    [InlineData("system.log.view", PermissionCategory.Technical)]
    [InlineData("system.performance.view", PermissionCategory.Technical)]
    [InlineData("system.signalr.view", PermissionCategory.Technical)]
    [InlineData("storage.chunk.view", PermissionCategory.Technical)]
    [InlineData("storage.version.view", PermissionCategory.Technical)]
    [InlineData("ai.provider.view", PermissionCategory.Technical)]
    [InlineData("ai.mcp.view", PermissionCategory.Technical)]
    [InlineData("ai.quota.view", PermissionCategory.Technical)]
    [InlineData("ai.channels.view", PermissionCategory.Technical)]
    [InlineData("ai.sandbox.view", PermissionCategory.Technical)]
    [InlineData("ai.permissions.view", PermissionCategory.Technical)]
    [InlineData("ai.sql.execute", PermissionCategory.Technical)]
    // Dictionaries share the /admin/settings endpoint (gated by
    // system.parameter.view); the code stays registered but is Technical so a
    // business admin never gets it implicitly.
    [InlineData("system.dictionary.view", PermissionCategory.Technical)]
    // Business spot checks: the surfaces a business admin runs daily.
    [InlineData("user.view", PermissionCategory.Business)]
    [InlineData("role.view", PermissionCategory.Business)]
    [InlineData("dashboard.view", PermissionCategory.Business)]
    [InlineData("system.menu.view", PermissionCategory.Business)]
    [InlineData("storage.file.view", PermissionCategory.Business)]
    [InlineData("audit.log.view", PermissionCategory.Business)]
    [InlineData("finance.account.view", PermissionCategory.Business)]
    [InlineData("payment.order.view", PermissionCategory.Business)]
    [InlineData("ai.agent.view", PermissionCategory.Business)]
    [InlineData("ai.usage.view", PermissionCategory.Business)]
    public void Define_classifies_code_with_expected_category(string code, PermissionCategory expected)
    {
        var context = BuildContext();
        context.Permissions[code].Category.ShouldBe(expected);
    }

    [Fact]
    public void Technical_code_count_is_locked()
    {
        var context = BuildContext();
        var technical = context.Permissions.Values
            .Where(p => p.Category == PermissionCategory.Technical)
            .Select(p => p.Name)
            .ToList();

        // Deliberate-bump lock, same rationale as the total count: a code
        // silently flipping category changes what business admins can reach.
        technical.Count.ShouldBe(27);
    }

    [Theory]
    [InlineData("identity.view")]
    [InlineData("user.view")]
    [InlineData("role.view")]
    [InlineData("tenant.view")]
    [InlineData("authorization.roleFunction.view")]
    [InlineData("system.view")]
    [InlineData("system.menu.view")]
    [InlineData("dashboard.view")]
    [InlineData("feature.view")]
    [InlineData("storage.file.view")]
    [InlineData("audit.log.view")]
    [InlineData("notification.message.view")]
    [InlineData("chat.session.view")]
    [InlineData("payment.order.view")]
    [InlineData("finance.account.view")]
    [InlineData("finance.journal.view")]
    [InlineData("finance.report.view")]
    [InlineData("template.layout.view")]
    [InlineData("ai.agent.view")]
    [InlineData("ai.thread.view")]
    public void Define_includes_admin_route_permission_code(string code)
    {
        var context = BuildContext();
        context.Permissions.ContainsKey(code).ShouldBeTrue(
            $"FrameworkPermissions must declare '{code}' (referenced by an admin route's meta.permission).");
    }

    [Fact]
    public void Every_permission_resolves_to_a_declared_group()
    {
        var context = BuildContext();
        foreach (var permission in context.Permissions.Values)
        {
            var groupCode = permission.ParentName;
            groupCode.ShouldNotBeNullOrEmpty(
                $"Permission '{permission.Name}' must declare a parent group via parentName.");
            context.Groups.ContainsKey(groupCode!).ShouldBeTrue(
                $"Permission '{permission.Name}' references group '{groupCode}' which is not declared.");
        }
    }
}
