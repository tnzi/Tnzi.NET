using System.Reflection;
using Tnzi.Finance.Payroll.Controllers.Admin;
using Tnzi.Security.Authorization;

namespace Tnzi.Finance.Payroll.Tests;

/// <summary>
/// 外部批次摄取端点必须同时要 <c>payroll.run.create</c> 与 <c>payroll.run.update</c>。
/// </summary>
/// <remarks>
/// External 批次在 <c>Payroll:ExternalAutoPost</c>（默认 true）下摄取即过账，同一
/// <c>ProviderRunId</c> 再次摄取也会补投过账；内部批次的计算与过账一律要 <c>.update</c>。
/// 此前只挂 <c>.create</c>：只负责准备批次的角色经这一个端点把任意金额的工资费用与应付
/// 记进总账，或写下决定此后每期法定上限的期初 YTD。方法级多个特性是 AND 关系。
/// </remarks>
public class PayRunExternalIngestGateTests
{
    [Fact]
    public void ExternalIngest_Requires_Both_Create_And_Update()
    {
        var method = typeof(DefaultPayrollPayRunAdminController)
            .GetMethod(nameof(DefaultPayrollPayRunAdminController.CreateFromExternal), BindingFlags.Public | BindingFlags.Instance);
        method.ShouldNotBeNull();

        var codes = method.GetCustomAttributes<ApiAuthorizeAttribute>(inherit: true)
            .Select(a => a.PermissionName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        codes.ShouldContain("payroll.run.create");
        codes.ShouldContain("payroll.run.update",
            "ingesting an external run auto-posts it, and posting an internal run requires payroll.run.update");
    }

    /// <summary>反方向：起草内部批次仍只要 <c>.create</c>（草稿没有任何账务效果）。</summary>
    [Fact]
    public void Creating_A_Draft_Run_Stays_Under_The_Create_Code_Alone()
    {
        var method = typeof(DefaultPayrollPayRunAdminController)
            .GetMethod(nameof(DefaultPayrollPayRunAdminController.Create), BindingFlags.Public | BindingFlags.Instance);
        method.ShouldNotBeNull();

        method.GetCustomAttributes<ApiAuthorizeAttribute>(inherit: true)
            .Select(a => a.PermissionName)
            .ShouldNotContain("payroll.run.update");
    }
}
