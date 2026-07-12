namespace Tnzi.Authorization.Permissions;

/// <summary>
/// Operation-level permission codes for the Authorization module's admin surfaces.
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
public class AuthorizationPermissions : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        // WHOLE MODULE is marked Technical: managing "who can do what" (roles ->
        // functions, the function/permission catalogue, entity-role data rules)
        // is a security/system concern, not business administration.
        context.AddGroup("authorization", "Authorization", defaultCategory: PermissionCategory.Technical);
        context.AddPermission("authorization.view", "View Authorization", parentName: "authorization");
        context.AddCrudPermissions("authorization.functionModule", "Function Modules", parentName: "authorization");
        context.AddCrudPermissions("authorization.permission", "Permissions", parentName: "authorization");
        // Role-permission granting is one dedicated action, not crud: every
        // write on the role-functions surface (assign/remove/set/clear/clone/
        // import) is "changing what a role can do". The delegation guard
        // (grantable subset of grantor's own set) applies on top of this code.
        context.AddPermission("authorization.roleFunction.view", "View Role Functions", parentName: "authorization");
        context.AddPermission("authorization.roleFunction.assign", "Assign Role Permissions", parentName: "authorization");
        // User-direct granting mirrors the role surface: one dedicated assign
        // action for every write (assign/remove/set/clear) on a user's direct
        // grants. The same delegation guard (grantable subset of grantor's own
        // set) applies on top of this code.
        context.AddPermission("authorization.userFunction.view", "View User Functions", parentName: "authorization");
        context.AddPermission("authorization.userFunction.assign", "Assign User Permissions", parentName: "authorization");
        context.AddCrudPermissions("authorization.entityRole", "Entity Roles", parentName: "authorization");
    }
}
