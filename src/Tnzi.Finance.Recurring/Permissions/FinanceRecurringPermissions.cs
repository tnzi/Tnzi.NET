namespace Tnzi.Finance.Recurring.Permissions;

/// <summary>
/// 周期性单据权限码
/// </summary>
/// <remarks>
/// 自带一套码而不是复用 <c>finance.document.*</c>：**能改一条模板的人，等于能改
/// 未来每一期的金额**，这与"能开一张发票"不是同一个风险等级。生成（execute）
/// 另立一码 —— 手工触发会立刻造出真单据。
///
/// 权限码随模块走：不加载本子模块的宿主不会 seed 这几个码。
/// </remarks>
public class FinanceRecurringPermissions : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        Check.NotNull(context);

        context.AddCrudPermissions("finance.recurring", "Recurring Documents", parentName: "finance");
        context.AddPermission("finance.recurring.execute", "Run Recurring Documents", parentName: "finance");
    }
}
