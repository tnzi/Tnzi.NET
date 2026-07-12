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
    public void Define(IPermissionDefinitionContext context)
    {
        context.AddGroup("payment", "Payment");
        context.AddPermission("payment.view", "View Payment", parentName: "payment");
        // Orders are created by the payment flow; admin writes are lifecycle
        // transitions (close/sync) = update.
        context.AddCrudPermissions("payment.order", "Orders", parentName: "payment", actions: CrudActions.View | CrudActions.Update);
        context.AddCrudPermissions("payment.refund", "Refunds", parentName: "payment", actions: CrudActions.View | CrudActions.Update);
        context.AddCrudPermissions("payment.subscription", "Payment Subscriptions", parentName: "payment");
        context.AddCrudPermissions("payment.invoice", "Invoices", parentName: "payment", actions: CrudActions.View | CrudActions.Create | CrudActions.Update);
        context.AddCrudPermissions("payment.promotion", "Promotions", parentName: "payment", actions: CrudActions.View | CrudActions.Create | CrudActions.Update);
        context.AddPermission("payment.statistics.view", "View Payment Statistics", parentName: "payment");
    }
}
