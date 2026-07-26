namespace Tnzi.Finance.Permissions;

/// <summary>
/// Operation-level permission codes for the Finance module's admin surfaces.
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
public class FinancePermissions : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        context.AddGroup("finance", "Finance");
        context.AddPermission("finance.view", "View Finance", parentName: "finance");
        context.AddCrudPermissions("finance.account", "Chart of Accounts", parentName: "finance");
        context.AddCrudPermissions("finance.journal", "Journal Entries", parentName: "finance");
        // Rates are date-keyed upserts (manual + provider refresh) = create.
        context.AddCrudPermissions("finance.rate", "Exchange Rates", parentName: "finance", actions: CrudActions.View | CrudActions.Create | CrudActions.Delete);
        context.AddCrudPermissions("finance.fiscalYear", "Fiscal Years", parentName: "finance");
        // 封账日与会计年度是两把正交的锁，各自独立授权：能开关年度的人不一定该能
        // 推动滚动封账线（后者直接决定"报出去的数字还能不能被改"）。
        context.AddPermission("finance.ledgerLock.view", "View Closing Date", parentName: "finance");
        context.AddPermission("finance.ledgerLock.update", "Change Closing Date", parentName: "finance");
        context.AddPermission("finance.report.view", "View Financial Reports", parentName: "finance");
        context.AddCrudPermissions("finance.customer", "Customers", parentName: "finance");
        context.AddCrudPermissions("finance.vendor", "Vendors", parentName: "finance");
        context.AddCrudPermissions("finance.item", "Items", parentName: "finance");
        context.AddCrudPermissions("finance.tax", "Taxes", parentName: "finance");
        context.AddCrudPermissions("finance.document", "Finance Documents", parentName: "finance");
        // 对账单与催收：看得到账龄不等于该能把对账单寄出去/下载出去。
        context.AddPermission("finance.statement.view", "Customer Statements", parentName: "finance");
        // 附件与讨论各自成套：能看单据不等于该看得到内部讨论（那里常写着不适合
        // 外传的判断），也不等于该能把别人挂的凭据摘下来。
        context.AddCrudPermissions("finance.attachment", "Document Attachments", parentName: "finance",
            actions: CrudActions.View | CrudActions.Create | CrudActions.Delete);
        context.AddCrudPermissions("finance.comment", "Document Comments", parentName: "finance",
            actions: CrudActions.View | CrudActions.Create | CrudActions.Delete);
        // 报价单与采购订单是**成为会计事实之前**的单据：报价的销售、下单的采购，
        // 都不该因此拿到发票/账单的过账权限，所以自带一套码而不是复用 finance.document.*。
        // 转换动作（转发票/转账单）叠加目标单据的 .create，见控制器。
        context.AddCrudPermissions("finance.estimate", "Estimates", parentName: "finance");
        context.AddCrudPermissions("finance.purchaseOrder", "Purchase Orders", parentName: "finance");
        // Complete（锁定对账）走 .update（生命周期状态变更），与单据 post/void 一致。
        context.AddCrudPermissions("finance.reconciliation", "Bank Reconciliations", parentName: "finance");
        // 期末汇兑重估：非 CRUD 套装（view 只读预览 + execute 过账）。
        context.AddPermission("finance.revaluation.view", "FX Revaluation", parentName: "finance");
        context.AddPermission("finance.revaluation.execute", "Run FX Revaluation", parentName: "finance");
        // 余额汇总运维：非 CRUD 套装（view 只读校验 + execute 全量重建）。
        context.AddPermission("finance.balanceSummary.view", "Balance Summary", parentName: "finance");
        context.AddPermission("finance.balanceSummary.execute", "Rebuild Balance Summary", parentName: "finance");

    }
}
