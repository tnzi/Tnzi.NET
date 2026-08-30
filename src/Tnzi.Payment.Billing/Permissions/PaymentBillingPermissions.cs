namespace Tnzi.Payment.Billing.Permissions;

/// <summary>
/// 发票 admin 面的操作级权限码（3 个）。
/// </summary>
/// <remarks>
/// 码串与拆分前**逐字节相同**（<c>payment.invoice.view</c> / <c>.create</c> / <c>.update</c>）：
/// 权限码是持久化契约，已授出去的角色行按码串匹配，改名等于把所有人的授权静默清空；
/// 管理端路由的 <c>meta.permission</c> 也是把它们当字面量写死的。
///
/// 父组仍是核心 <c>PaymentPermissions</c> 声明的 <c>payment</c>，本模块**不再 AddGroup 一次**
/// （沿 <c>FinanceOffersPermissions</c> / <c>FinanceBankingPermissions</c> 的做法）：
/// 组由父模块声明、子模块只往里挂码，两块在授权界面里合并成一棵树。
/// <c>AddGroup</c> 是 first-wins 的，重复声明本身不报错，但会让组的显示名取决于模块加载顺序。
///
/// 没有 <c>.delete</c>：发票不删，作废走 <c>cancel</c>（<c>.update</c>）—— 一张发出去的发票
/// 对方手里有一份，删掉等于让票号出现谁也解释不了的缺口。这与拆分前一致。
///
/// 随模块走：不开票的宿主永远不会 seed 这 3 个码，权限矩阵里也不会多出一块永远不授予的功能面。
/// </remarks>
public class PaymentBillingPermissions : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        context.AddCrudPermissions("payment.invoice", "Invoices", parentName: "payment",
            actions: CrudActions.View | CrudActions.Create | CrudActions.Update);
    }
}
