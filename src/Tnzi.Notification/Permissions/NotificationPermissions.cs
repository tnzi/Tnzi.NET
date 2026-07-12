namespace Tnzi.Notification.Permissions;

/// <summary>
/// Operation-level permission codes for the Notification module's admin surfaces.
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
public class NotificationPermissions : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        context.AddGroup("notification", "Notification");
        context.AddPermission("notification.view", "View Notification", parentName: "notification");
        context.AddCrudPermissions("notification.message", "Notification Messages", parentName: "notification");
        // Preferences are per-user upserts - no standalone create.
        context.AddCrudPermissions("notification.subscription", "Subscriptions", parentName: "notification", actions: CrudActions.View | CrudActions.Update | CrudActions.Delete);
        context.AddCrudPermissions("notification.template", "Notification Templates", parentName: "notification");
    }
}
