namespace Tnzi.System.Permissions;

/// <summary>
/// Operation-level permission codes for the System module's admin surfaces.
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
public class SystemPermissions : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        // Shared "system" group - AddGroup is idempotent (first wins), so every
        // module contributing ops/infrastructure surfaces declares it with the
        // same arguments. Technical default: these are ops surfaces, not
        // business administration.
        context.AddGroup("system", "System", defaultCategory: PermissionCategory.Technical);
        context.AddPermission("system.view", "View System", parentName: "system");
        context.AddCrudPermissions("system.menu", "Menus", parentName: "system");
        context.AddCrudPermissions("system.parameter", "Parameters", parentName: "system");
        // Global admin theme: an opaque JSON snapshot edited through the admin
        // theme drawer; applies to every signed-in user. Deny-by-default keeps
        // it super-admin-only unless explicitly delegated.
        context.AddCrudPermissions("system.appearance", "Appearance", parentName: "system", actions: CrudActions.View | CrudActions.Update);
        context.AddCrudPermissions("system.accessLog", "Access Logs", parentName: "system", actions: CrudActions.View | CrudActions.Create | CrudActions.Delete);
        // Dictionaries share the /admin/settings endpoints - writes are gated
        // by system.parameter.* codes; this code only gates page visibility.
        context.AddPermission("system.dictionary.view", "View Dictionaries", parentName: "system");
    }
}
