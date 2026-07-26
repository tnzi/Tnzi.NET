namespace Tnzi.AI.Mcp.Permissions;

/// <summary>
/// Operation-level permission codes for the AI MCP Server sub-module's admin surfaces.
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
/// <para>
/// <b>Server side vs client side.</b> This sub-module exposes framework agents
/// <i>as</i> an MCP server and owns <c>ai.mcpServer.*</c>. The external MCP
/// <i>client</i> registry (the servers the framework connects TO) lives in the
/// AI core and keeps <c>ai.mcp.*</c> in <c>Tnzi.AI.Permissions.AIPermissions</c>.
/// The two are authorized independently so "manage which agents are exposed"
/// can be granted separately from "manage external MCP server registrations".
/// </para>
/// </remarks>
public class AIMcpPermissions : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        // Shared "ai" group - AddGroup is idempotent (first wins), so the AI
        // core module and every AI sub-module declare it with the same arguments.
        context.AddGroup("ai", "AI");

        // Self-hosted MCP server management: view status/tools, expose/remove
        // agents (.update), prune tool-analytics records (.delete). No .create -
        // exposing an agent mutates the server's exposure config, it is not a
        // new resource. Technical: an ops surface, mirrors the ai.mcp client
        // registry category.
        context.AddCrudPermissions("ai.mcpServer", "MCP Server", parentName: "ai",
            category: PermissionCategory.Technical,
            actions: CrudActions.View | CrudActions.Update | CrudActions.Delete);
    }
}
