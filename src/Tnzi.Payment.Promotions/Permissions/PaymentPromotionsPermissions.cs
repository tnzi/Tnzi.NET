namespace Tnzi.Payment.Promotions.Permissions;

/// <summary>
/// 促销 admin 面的操作级权限码（3 个）。
/// </summary>
/// <remarks>
/// 码串与拆分前<b>逐字节相同</b>（<c>payment.promotion.view</c> / <c>.create</c> / <c>.update</c>）：
/// 权限码是持久化契约，已授出去的角色行按码串匹配，改名等于把所有人的授权静默清空；
/// 管理端路由的 <c>meta.permission</c> 也是把它们当字面量写死的。
///
/// 父组仍是核心 <c>PaymentPermissions</c> 声明的 <c>payment</c>，本模块**不再 AddGroup 一次**
/// （沿 <c>PaymentBillingPermissions</c> / <c>PaymentSubscriptionsPermissions</c> 的做法）：
/// 组由父模块声明、子模块只往里挂码，两块在授权界面里合并成一棵树。
/// <c>AddGroup</c> 是 first-wins 的，重复声明本身不报错，但会让组的显示名取决于模块加载顺序。
///
/// <b>没有 <c>.delete</c></b>，与拆分前一致：促销只停用不删除（<c>DeactivateAsync</c> 走
/// <c>.update</c>）—— 一条已经被人核销过的促销删掉，核销记录就成了指向虚空的孤儿。
///
/// 随模块走：从不打折的宿主永远不会 seed 这 3 个码，权限矩阵里也不会多出一块永远不授予的功能面。
/// </remarks>
public class PaymentPromotionsPermissions : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        context.AddCrudPermissions("payment.promotion", "Promotions", parentName: "payment",
            actions: CrudActions.View | CrudActions.Create | CrudActions.Update);
    }
}
