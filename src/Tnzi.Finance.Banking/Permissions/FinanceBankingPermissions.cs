namespace Tnzi.Finance.Banking.Permissions;

/// <summary>
/// 银行域 admin 面的操作级权限码（23 个）。
/// </summary>
/// <remarks>
/// 随模块走：不加载银行域的宿主永远不会 seed 这些码，权限矩阵里也不会多出一整块
/// 用不上的功能面。父组仍是核心声明的 <c>finance</c>，因此两块在授权界面里合并成一棵树。
/// </remarks>
public class FinanceBankingPermissions : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        context.AddCrudPermissions("finance.bankAccount", "Bank Accounts", parentName: "finance");
        context.AddCrudPermissions("finance.bankFeed", "Bank Feed", parentName: "finance");
        // 银行规则是运维配置：能改规则的人等于能决定钱自动记到哪个科目，
        // 与"能看流水、能确认匹配"不是一回事，故单列一套码。
        context.AddCrudPermissions("finance.bankRule", "Bank Rules", parentName: "finance");
        // 支票：占号留痕，无删除端点。
        context.AddCrudPermissions("finance.check", "Checks", parentName: "finance", actions: CrudActions.View | CrudActions.Create | CrudActions.Update);
        context.AddCrudPermissions("finance.receipt", "Receipts", parentName: "finance");
        context.AddCrudPermissions("finance.partyBank", "Party Bank Accounts", parentName: "finance");
        // EFT：写三件套 + 独立 download（导出文件含全量明文账号，与 view 分离）。
        context.AddCrudPermissions("finance.eft", "EFT Batches", parentName: "finance", actions: CrudActions.View | CrudActions.Create | CrudActions.Update);
        context.AddPermission("finance.eft.download", "Download EFT Files", parentName: "finance");
    }
}
