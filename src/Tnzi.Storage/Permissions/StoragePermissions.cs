namespace Tnzi.Storage.Permissions;

/// <summary>
/// The Storage permission codes as constants, so the runtime access checks in
/// <see cref="Tnzi.Storage.Services.FileAccessAuthorizer"/> and the controller
/// attributes cannot drift apart from the declarations below.
/// </summary>
public static class StoragePermissionNames
{
    public const string View = "storage.view";
    public const string FileView = "storage.file.view";
    public const string FileCreate = "storage.file.create";
    public const string FileUpdate = "storage.file.update";
    public const string FileDelete = "storage.file.delete";
    public const string ChunkView = "storage.chunk.view";
    public const string VersionView = "storage.version.view";
}

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
