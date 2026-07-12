namespace Tnzi.Chat.Permissions;

/// <summary>
/// Operation-level permission codes for the Chat module's admin surfaces.
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
public class ChatPermissions : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        context.AddGroup("chat", "Chat");
        context.AddPermission("chat.view", "View Chat", parentName: "chat");
        // create = broadcast (delivers system messages); delete = remove
        // conversations / recall messages. No admin-side update surface.
        context.AddCrudPermissions("chat.session", "Chat Sessions", parentName: "chat", actions: CrudActions.View | CrudActions.Create | CrudActions.Delete);
    }
}
