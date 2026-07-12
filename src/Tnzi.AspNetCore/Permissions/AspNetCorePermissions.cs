namespace Tnzi.AspNetCore.Permissions;

/// <summary>
/// Operation-level permission codes for the AspNetCore module's (diagnostics / log viewer) admin surfaces.
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
public class AspNetCorePermissions : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        // Shared "system" group - AddGroup is idempotent (first wins), so every
        // module contributing ops/infrastructure surfaces declares it with the
        // same arguments. Technical default: these are ops surfaces, not
        // business administration.
        context.AddGroup("system", "System", defaultCategory: PermissionCategory.Technical);
        context.AddPermission("system.diagnostics.view", "View Diagnostics", parentName: "system");
        // Trigger-style destructive action: clears the in-memory exception statistics buffer.
        context.AddPermission("system.diagnostics.execute", "Clear Diagnostics Data", parentName: "system");
        context.AddPermission("system.log.view", "View Logs", parentName: "system");
    }
}
