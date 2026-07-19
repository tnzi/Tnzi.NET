namespace Tnzi.Finance.Payroll.Permissions;

/// <summary>
/// Operation-level permission codes for the Payroll sub-module's admin surfaces.
/// </summary>
/// <remarks>
/// Declared in-module per docs/coding-standards/permissions.md: loading the
/// module brings its catalogue along, and hosts that do not load it never
/// seed these codes. On startup the Authorization module's
/// <c>PermissionDbSeeder</c> collects every registered provider and upserts
/// the declarations as system-managed rows (no-op when Authorization is not
/// loaded). All 14 codes are declared up front: the pay run and country pack
/// endpoints ship in P4c, but the catalogue is versioned as one unit so role
/// setups done today survive the P4c rollout unchanged.
/// </remarks>
public class PayrollPermissions : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        context.AddGroup("payroll", "Payroll");
        context.AddPermission("payroll.view", "View Payroll", parentName: "payroll");
        context.AddCrudPermissions("payroll.employee", "Employees", parentName: "payroll");
        // 组件/结构/税级表三资源合一（同属薪酬配置面）
        context.AddCrudPermissions("payroll.config", "Payroll Configuration", parentName: "payroll");
        // calculate/post/pay/void 走 .update（生命周期状态变更）；external 摄取走 .create
        context.AddCrudPermissions("payroll.run", "Pay Runs", parentName: "payroll");
        context.AddPermission("payroll.pack.execute", "Seed Country Packs", parentName: "payroll");
    }
}
