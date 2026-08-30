namespace Tnzi.Payment.Subscriptions.Permissions;

/// <summary>
/// 订阅 admin 面的操作级权限码（4 个）。
/// </summary>
/// <remarks>
/// 码串与拆分前**逐字节相同**（<c>payment.subscription.view</c> / <c>.create</c> / <c>.update</c> /
/// <c>.delete</c>）：权限码是持久化契约，已授出去的角色行按码串匹配，改名等于把所有人的授权
/// 静默清空；管理端路由的 <c>meta.permission</c> 也是把它们当字面量写死的。
///
/// 父组仍是核心 <c>PaymentPermissions</c> 声明的 <c>payment</c>，本模块**不再 AddGroup 一次**
/// （沿 <c>PaymentBillingPermissions</c> / <c>FinanceOffersPermissions</c> 的做法）：
/// 组由父模块声明、子模块只往里挂码，两块在授权界面里合并成一棵树。
/// <c>AddGroup</c> 是 first-wins 的，重复声明本身不报错，但会让组的显示名取决于模块加载顺序。
///
/// 四个动作齐全（含 <c>.delete</c>）：删的是**订阅计划**（一张还没人订过的价目表行），
/// 不是订阅本身 —— 订阅只取消不删除，取消走 <c>.update</c>。计划上带着
/// 「已有活跃订阅则拒绝删除」的守卫，所以 <c>.delete</c> 是一个真的、且有边界的能力。
///
/// 随模块走：只做一次性收款的宿主永远不会 seed 这 4 个码，权限矩阵里也不会多出一块
/// 永远不授予的功能面。
/// </remarks>
public class PaymentSubscriptionsPermissions : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        context.AddCrudPermissions("payment.subscription", "Payment Subscriptions", parentName: "payment");
    }
}
