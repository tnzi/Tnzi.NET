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
        codes.Count.ShouldBe(69);
        context.Groups.Count.ShouldBe(10);
    }

    [Theory]
    [InlineData("identity.view")]
    [InlineData("user.view")]
    [InlineData("role.view")]
    [InlineData("tenant.view")]
    [InlineData("authorization.roleFunction.view")]
    [InlineData("system.view")]
    [InlineData("system.menu.view")]
    [InlineData("workbench.view")]
    [InlineData("feature.view")]
    [InlineData("storage.file.view")]
    [InlineData("audit.log.view")]
    [InlineData("notification.message.view")]
    [InlineData("chat.session.view")]
    [InlineData("payment.order.view")]
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
