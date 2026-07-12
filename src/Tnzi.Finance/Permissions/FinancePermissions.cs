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
    }
}
