namespace Tnzi.Identity.Permissions;

/// <summary>
/// Operation-level permission codes for the Identity module's admin surfaces.
/// </summary>
/// <remarks>
/// Declared in-module per docs/coding-standards/permissions.md: loading the
/// module brings its catalogue along, and hosts that do not load it never
/// seed these codes. On startup the Authorization module's
/// <c>PermissionDbSeeder</c> collects every registered provider and upserts
/// the declarations as system-managed rows (no-op when Authorization is not
/// loaded). Codes are word-for-word identical to the admin routes'
/// <c>meta.permission</c> values; admin controllers enforce them as
/// class-level <c>.view</c> AND method-level write codes.
/// </remarks>
public class IdentityPermissions : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        context.AddGroup("identity", "Identity");
        context.AddPermission("identity.view", "View Identity", parentName: "identity");
        context.AddCrudPermissions("user", "Users", parentName: "identity");
        context.AddCrudPermissions("role", "Roles", parentName: "identity");
        context.AddCrudPermissions("tenant", "Tenants", parentName: "identity", category: PermissionCategory.Technical);
        context.AddCrudPermissions("organization", "Organizations", parentName: "identity");
        // Sessions: no create (sessions are born by signing in); revoke/clean = delete.
        context.AddCrudPermissions("session", "Sessions", parentName: "identity", category: PermissionCategory.Technical, actions: CrudActions.View | CrudActions.Update | CrudActions.Delete);
        // Login logs (IP / user-agent / failure reasons) are the same genre as
        // system.accessLog - security-audit monitoring, not business data; and
        // login security is lockout/risk-policy monitoring. Both Technical.
        context.AddCrudPermissions("identity.loginLog", "Login Logs", parentName: "identity", category: PermissionCategory.Technical, actions: CrudActions.View | CrudActions.Delete);
        context.AddPermission("identity.loginSecurity.view", "View Login Security", parentName: "identity", category: PermissionCategory.Technical);
    }
}
