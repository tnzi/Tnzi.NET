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
        context.AddPermission("finance.report.view", "View Financial Reports", parentName: "finance");
        context.AddCrudPermissions("finance.customer", "Customers", parentName: "finance");
        context.AddCrudPermissions("finance.vendor", "Vendors", parentName: "finance");
        context.AddCrudPermissions("finance.item", "Items", parentName: "finance");
        context.AddCrudPermissions("finance.tax", "Taxes", parentName: "finance");
        context.AddCrudPermissions("finance.document", "Finance Documents", parentName: "finance");
        // Complete（锁定对账）走 .update（生命周期状态变更），与单据 post/void 一致。
        context.AddCrudPermissions("finance.reconciliation", "Bank Reconciliations", parentName: "finance");
        // 期末汇兑重估：非 CRUD 套装（view 只读预览 + execute 过账）。
        context.AddPermission("finance.revaluation.view", "FX Revaluation", parentName: "finance");
        context.AddPermission("finance.revaluation.execute", "Run FX Revaluation", parentName: "finance");
        // 余额汇总运维：非 CRUD 套装（view 只读校验 + execute 全量重建）。
        context.AddPermission("finance.balanceSummary.view", "Balance Summary", parentName: "finance");
        context.AddPermission("finance.balanceSummary.execute", "Rebuild Balance Summary", parentName: "finance");

        // P3「输出与摄取」的 6 个 admin 面（一次性声明全套 23 码）。
        context.AddCrudPermissions("finance.bankAccount", "Bank Accounts", parentName: "finance");
        context.AddCrudPermissions("finance.bankFeed", "Bank Feed", parentName: "finance");
        // 支票：占号留痕，无删除端点。
        context.AddCrudPermissions("finance.check", "Checks", parentName: "finance", actions: CrudActions.View | CrudActions.Create | CrudActions.Update);
        context.AddCrudPermissions("finance.receipt", "Receipts", parentName: "finance");
        context.AddCrudPermissions("finance.partyBank", "Party Bank Accounts", parentName: "finance");
        // EFT：写三件套 + 独立 download（导出文件含全量明文账号，与 view 分离）。
        context.AddCrudPermissions("finance.eft", "EFT Batches", parentName: "finance", actions: CrudActions.View | CrudActions.Create | CrudActions.Update);
        context.AddPermission("finance.eft.download", "Download EFT Files", parentName: "finance");
    }
}
