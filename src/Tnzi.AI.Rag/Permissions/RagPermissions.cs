namespace Tnzi.AI.Rag.Permissions;

/// <summary>
/// Operation-level permission codes for the AI RAG sub-module's (knowledge bases) admin surfaces.
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
public class RagPermissions : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        // Shared "ai" group - AddGroup is idempotent (first wins), so the AI
        // core module and every AI sub-module declare it with the same arguments.
        context.AddGroup("ai", "AI");
        context.AddCrudPermissions("ai.knowledge", "Knowledge", parentName: "ai");
    }
}
