namespace Tnzi.Storage.Permissions;

/// <summary>
/// Operation-level permission codes for the Storage module's admin surfaces.
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
public class StoragePermissions : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        context.AddGroup("storage", "Storage");
        context.AddPermission("storage.view", "View Storage", parentName: "storage");
        context.AddCrudPermissions("storage.file", "Files", parentName: "storage");
        context.AddPermission("storage.chunk.view", "View Chunks", parentName: "storage", category: PermissionCategory.Technical);
        context.AddPermission("storage.version.view", "View Versions", parentName: "storage", category: PermissionCategory.Technical);
    }
}
