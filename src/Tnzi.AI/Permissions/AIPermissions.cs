namespace Tnzi.AI.Permissions;

/// <summary>
/// Operation-level permission codes for the AI core module's admin surfaces.
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
public class AIPermissions : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        // Shared "ai" group - AddGroup is idempotent (first wins), so the AI
        // core module and every AI sub-module declare it with the same arguments.
        context.AddGroup("ai", "AI");
        context.AddPermission("ai.view", "View AI", parentName: "ai");
        context.AddCrudPermissions("ai.agent", "Agents", parentName: "ai");
        context.AddPermission("ai.agent.execute", "Run Agents", parentName: "ai");
        // Run monitoring exposes execution traces / tool calls / kill-and-
        // send-input control signals - an ops surface, not a business one.
        context.AddPermission("ai.agentRun.view", "View Agent Runs", parentName: "ai", category: PermissionCategory.Technical);
        context.AddPermission("ai.agentRun.execute", "Control Agent Runs", parentName: "ai", category: PermissionCategory.Technical);
        context.AddCrudPermissions("ai.persona", "Personas", parentName: "ai");
        context.AddCrudPermissions("ai.provider", "Providers", parentName: "ai", category: PermissionCategory.Technical);
        context.AddPermission("ai.provider.execute", "Test Providers", parentName: "ai", category: PermissionCategory.Technical);
        context.AddCrudPermissions("ai.mcp", "MCP Servers", parentName: "ai", category: PermissionCategory.Technical);
        context.AddPermission("ai.mcp.execute", "Test MCP Servers", parentName: "ai", category: PermissionCategory.Technical);
        // Quotas are per-user upserts (set/reset) = update.
        context.AddCrudPermissions("ai.quota", "Quotas", parentName: "ai", category: PermissionCategory.Technical, actions: CrudActions.View | CrudActions.Update);
        context.AddPermission("ai.usage.view", "View AI Usage", parentName: "ai");
        // Evaluations are prompt/quality-engineering benchmarks - Technical.
        context.AddCrudPermissions("ai.evaluation", "Evaluations", parentName: "ai", category: PermissionCategory.Technical, actions: CrudActions.View | CrudActions.Delete);
        context.AddPermission("ai.evaluation.execute", "Run Evaluations", parentName: "ai", category: PermissionCategory.Technical);
        context.AddCrudPermissions("ai.permissions", "AI Permissions", parentName: "ai", category: PermissionCategory.Technical);
        context.AddCrudPermissions("ai.thread", "Threads", parentName: "ai");
        context.AddPermission("ai.sql.execute", "Execute AI SQL Queries", parentName: "ai", category: PermissionCategory.Technical);
    }
}
