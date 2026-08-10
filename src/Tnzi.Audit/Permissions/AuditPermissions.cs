namespace Tnzi.Audit.Permissions;

/// <summary>
/// Operation-level permission codes for the Audit module's admin surfaces.
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
public class AuditPermissions : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        context.AddGroup("audit", "Audit");
        context.AddPermission("audit.view", "View Audit", parentName: "audit");
        // Request-level audit (paths / status codes / durations) is technical
        // monitoring; the operation trail ("who changed which record") stays
        // Business - it is the compliance view business owners actually read.
        context.AddPermission("audit.log.view", "View Audit Logs", parentName: "audit", category: PermissionCategory.Technical);
        context.AddCrudPermissions("audit.operation", "Audit Operations", parentName: "audit", actions: CrudActions.View | CrudActions.Delete);

        // Destruction certificates are the evidence that a retention policy was
        // honoured, so reading them is a Business concern (auditors and privacy
        // officers ask for them), while triggering a run is a privileged action
        // that permanently destroys data - it gets its own operation code rather
        // than riding on any of the CRUD ones.
        // Who read which record is the most sensitive view this module offers -
        // it answers "who opened this informant's file last month" - so it gets
        // its own code rather than riding on audit.operation.view.
        context.AddPermission("audit.recordAccess.view", "View Record Access Trail", parentName: "audit");
        context.AddPermission("audit.destruction.view", "View Destruction Certificates", parentName: "audit");
        context.AddPermission("audit.destruction.execute", "Run Data Destruction", parentName: "audit");
    }
}
