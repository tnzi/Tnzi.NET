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
    /// <summary>看全部外部执行记录（管理台）。</summary>
    /// <remarks>
    /// 服务层用它区分「管理台在看」与「用户在看自己的」，故声明与判定共用同一个字面量 ——
    /// 两处各写一份字符串，改名时漏掉一处的症状是权限静默失效而不是编译失败。
    /// </remarks>
    public const string CliRunView = "ai.cliRun.view";

    /// <summary>控制外部执行（取消等）。</summary>
    public const string CliRunExecute = "ai.cliRun.execute";

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

        context.AddPermission(CliRunView, "View CLI Runs",
            parentName: "ai", category: PermissionCategory.Technical);
        context.AddPermission(CliRunExecute, "Control CLI Runs",
            parentName: "ai", category: PermissionCategory.Technical);
    }
}
