namespace Tnzi.Finance.Offers.Permissions;

/// <summary>
/// 报价单 / 采购订单 admin 面的操作级权限码（8 个）。
/// </summary>
/// <remarks>
/// 码串与拆分前**逐字节相同**（<c>finance.estimate.*</c> / <c>finance.purchaseOrder.*</c>）：
/// 权限码是持久化契约，已授出去的角色行按码串匹配，改名等于把所有人的授权静默清空。
///
/// 父组仍是核心声明的 <c>finance</c>，本模块**不再 AddGroup 一次** —— 沿
/// <c>FinanceBankingPermissions</c> 的做法：组由核心声明，子模块只往里挂码，
/// 两块在授权界面里合并成一棵树。（<c>AddGroup</c> 是 first-wins 的，重复声明本身不报错，
/// 但目录 pact 要求每个 provider 只声明属于自己的组，重复声明会让组的显示名取决于模块加载顺序。）
///
/// 随模块走：不做报价与采购的宿主永远不会 seed 这 8 个码，权限矩阵里也不会多出
/// 两块永远不授予的功能面。
///
/// 为什么不复用 <c>finance.document.*</c>：报价的销售、下单的采购，都不该因此拿到
/// 发票账单的过账权限。转换端点（转发票 / 转账单）在这套码之上**叠加**目标单据的
/// <c>finance.document.create</c>，因为转换会凭空造出一张草稿；那个码由核心声明，
/// 这条跨模块复用是合法的（见控制器）。
/// </remarks>
public class FinanceOffersPermissions : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        context.AddCrudPermissions("finance.estimate", "Estimates", parentName: "finance");
        context.AddCrudPermissions("finance.purchaseOrder", "Purchase Orders", parentName: "finance");
    }
}
