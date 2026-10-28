namespace Tnzi.Payment.Permissions;

/// <summary>
/// Operation-level permission codes for the Payment module's admin surfaces.
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
public class PaymentPermissions : IPermissionDefinitionProvider
{
    /// <summary>查看本人所代表的计费账户（订阅、付款、发票、已保存的卡）。</summary>
    public const string AccountView = "payment.account.view";

    /// <summary>代表计费账户付款、开通 / 变更 / 取消订阅、增删卡片、申请退款。</summary>
    public const string AccountManage = "payment.account.manage";

    public void Define(IPermissionDefinitionContext context)
    {
        // 平台范围：收款、退款、计划定价、发票都是运营方的后台。payment.account.* 也在这个组里，
        // 它们由客户一侧的管理者经显式授权持有（服务层按 Payment:BillingAccount:Scope 判），
        // 两种超管旁路范围下都成立：Platform 下超管隐式持有，All 下更不必说。
        context.AddGroup("payment", "Payment").Scope = PermissionScope.Platform;
        context.AddPermission("payment.view", "View Payment", parentName: "payment");
        // Orders are created by the payment flow; admin writes are lifecycle
        // transitions (close/sync) = update.
        context.AddCrudPermissions("payment.order", "Orders", parentName: "payment", actions: CrudActions.View | CrudActions.Update);
        // Refunds gained a create action: support agents raise refunds on behalf
        // of customers, which is a distinct capability from approving one.
        context.AddCrudPermissions("payment.refund", "Refunds", parentName: "payment", actions: CrudActions.View | CrudActions.Create | CrudActions.Update);
        // payment.subscription.* 随续费域搬去了可选子模块 Tnzi.Payment.Subscriptions
        // （PaymentSubscriptionsPermissions，4 个码逐字节相同，父组仍是这里声明的 "payment"）。
        // payment.invoice.* 随发票域搬去了可选子模块 Tnzi.Payment.Billing
        // （PaymentBillingPermissions，码串逐字节相同，父组仍是这里声明的 "payment"）。
        // payment.promotion.* 随折扣域搬去了可选子模块 Tnzi.Payment.Promotions
        // （PaymentPromotionsPermissions，3 个码逐字节相同，父组仍是这里声明的 "payment"）。
        context.AddPermission("payment.statistics.view", "View Payment Statistics", parentName: "payment");
        // 用户面：只在 Installation / Tenant 范围下被检查（User 范围下每人恒可管理自己的账户）。
        context.AddPermission(AccountView, "View Billing Account", parentName: "payment");
        context.AddPermission(AccountManage, "Manage Billing Account", parentName: "payment");
    }
}
