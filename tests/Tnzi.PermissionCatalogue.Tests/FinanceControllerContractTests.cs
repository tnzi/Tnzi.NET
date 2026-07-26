using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Tnzi.AspNetCore.Mvc;
using Tnzi.Finance.Permissions;
using Tnzi.Finance.Banking.Permissions;
using Tnzi.Finance.Recurring.Permissions;
using Tnzi.Finance.Payroll.Permissions;
using Tnzi.Security.Authorization;
using Tnzi.TestBase;

namespace Tnzi.PermissionCatalogue.Tests;

/// <summary>
/// Finance / Payroll 控制器接线契约：把 Acme 手工 boot 冒烟（端点 401 而非 404）
/// 验证的接线不变量固化为测试。
/// </summary>
/// <remarks>
/// 覆盖两类静默失效：控制器缺 <see cref="DefaultControllerAttribute"/>（宿主永不激活 → 404）
/// 与路由前缀违反约定（带 api/ 前缀或脱离 admin 段）。第三类——权限门引用了未声明的码
/// （端点恒 403）——是全框架不变量，由 <see cref="PermissionCataloguePactTests"/> 的
/// 通用契约对全部模块程序集校验，不在此单模块重复。
/// </remarks>
public class FinanceControllerContractTests
{
    private static readonly Assembly[] TargetAssemblies =
    [
        typeof(FinancePermissions).Assembly,
        // 银行域自 2026-07-25 起是独立程序集，控制器契约同样适用于它
        typeof(FinanceBankingPermissions).Assembly,
        // 周期性单据同为独立程序集（2026-07-25），同一套控制器契约适用
        typeof(FinanceRecurringPermissions).Assembly,
        typeof(PayrollPermissions).Assembly,
    ];

    private static List<Type> GetAdminControllers()
        => TargetAssemblies
            .SelectMany(a => a.SafeGetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ApiAdminControllerBase).IsAssignableFrom(t))
            .OrderBy(t => t.FullName)
            .ToList();

    private static bool IsInModuleSegment(string route)
        => route == "admin/finance" || route.StartsWith("admin/finance/", StringComparison.Ordinal)
            || route == "admin/payroll" || route.StartsWith("admin/payroll/", StringComparison.Ordinal);

    [Fact]
    public void Admin_controllers_are_discovered()
    {
        // 哨兵：反射扫描本身失效（命名空间/基类变更）时给出显式失败而非空转全绿。
        GetAdminControllers().Count.ShouldBeGreaterThanOrEqualTo(31);
    }

    [Fact]
    public void Every_admin_controller_is_a_default_controller_with_conventional_route()
    {
        var violations = new List<string>();

        foreach (var controller in GetAdminControllers())
        {
            if (controller.GetCustomAttribute<DefaultControllerAttribute>() == null)
                violations.Add($"{controller.Name}: missing [DefaultController] - the hosting module will never activate it (endpoints 404).");

            var route = controller.GetCustomAttribute<RouteAttribute>()?.Template;
            if (string.IsNullOrWhiteSpace(route))
                violations.Add($"{controller.Name}: missing [Route].");
            else
            {
                if (route.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
                    violations.Add($"{controller.Name}: route '{route}' must not carry the api/ prefix (RoutePrefixConvention adds it).");
                // 段边界匹配：裸 StartsWith("admin/finance") 会把 "admin/financex" 误判为合法
                if (!IsInModuleSegment(route))
                    violations.Add($"{controller.Name}: route '{route}' is outside the module's admin/finance | admin/payroll segments.");
            }

            var classGate = controller.GetCustomAttributes<ApiAuthorizeAttribute>(inherit: false)
                .Select(a => a.PermissionName)
                .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
            if (classGate == null)
                violations.Add($"{controller.Name}: missing class-level [ApiAuthorize(PermissionName = ...)] module gate.");
        }

        violations.ShouldBeEmpty(string.Join("\n", violations));
    }
}
