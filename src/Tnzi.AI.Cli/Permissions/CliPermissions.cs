namespace Tnzi.AI.Cli.Permissions;

/// <summary>
/// 外部 CLI agent 管理面的权限码。
/// </summary>
/// <remarks>
/// 权限码随模块走：没加载本模块的宿主不会 seed 这几个码。全部归入
/// <see cref="PermissionCategory.Technical"/> —— 它们管的是「哪台机器能跑什么 CLI」，
/// 是运维面而不是业务面。
/// </remarks>
public class CliPermissions : IPermissionDefinitionProvider
{
    /// <inheritdoc />
    public void Define(IPermissionDefinitionContext context)
    {
        Check.NotNull(context);

        // 与 AIPermissions 同参声明；AddGroup 幂等，先到先得。
        context.AddGroup("ai", "AI");

        context.AddCrudPermissions("ai.cliRuntime", "CLI Agent Runtimes",
            parentName: "ai", category: PermissionCategory.Technical,
            actions: CrudActions.View | CrudActions.Update | CrudActions.Delete);
        context.AddPermission("ai.cliRuntime.execute", "Probe CLI Runtimes",
            parentName: "ai", category: PermissionCategory.Technical);

        context.AddCrudPermissions("ai.cliBinding", "CLI Agent Bindings",
            parentName: "ai", category: PermissionCategory.Technical,
            actions: CrudActions.View | CrudActions.Update | CrudActions.Delete);

        context.AddPermission("ai.cliRun.view", "View CLI Runs",
            parentName: "ai", category: PermissionCategory.Technical);
        context.AddPermission("ai.cliRun.execute", "Control CLI Runs",
            parentName: "ai", category: PermissionCategory.Technical);
    }
}
