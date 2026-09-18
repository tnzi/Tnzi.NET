using System.Reflection;
using Tnzi.Finance.Banking.Controllers.Admin;
using Tnzi.Security.Authorization;

namespace Tnzi.Finance.Tests;

/// <summary>
/// 支票管理控制器上「产出可流通票据」的端点必须走写码 <c>finance.check.create</c>。
/// </summary>
/// <remarks>
/// 预览渲染的是完整票面：对配了白纸票纸（<see cref="CheckStockType.Blank"/>）的档案，
/// 它解密真账号拼出可机读的 E-13B 磁码行 —— 水印挡得住人眼，挡不住读票机。
/// 同控制器把零副作用的 <c>{id}/render</c> 放在写码之下，理由正是「产出的是可流通票据」；
/// 样张与校准页留在 view 的理由则是「永不接触账号」。预览两条都不满足，故与 render 同级。
/// 断言挂在特性上而不是渲染字节上：渲染字节分不出两道门。
/// </remarks>
public class CheckAdminControllerGateTests
{
    [Theory]
    [InlineData(nameof(DefaultFinanceCheckAdminController.Preview))]
    [InlineData(nameof(DefaultFinanceCheckAdminController.Render))]
    [InlineData(nameof(DefaultFinanceCheckAdminController.Print))]
    public void Endpoints_That_Emit_A_Negotiable_Face_Require_The_Create_Code(string action)
    {
        var method = typeof(DefaultFinanceCheckAdminController).GetMethod(action, BindingFlags.Public | BindingFlags.Instance);
        method.ShouldNotBeNull();

        var codes = method.GetCustomAttributes<ApiAuthorizeAttribute>(inherit: true)
            .Select(a => a.PermissionName)
            .ToList();

        codes.ShouldContain("finance.check.create",
            $"{action} renders a negotiable cheque face (a real MICR band on blank stock) and must not be reachable with finance.check.view alone");
    }

    /// <summary>样张与校准页永不接触账号，刻意留在类级 view 门下 —— 这条锁住反方向，别顺手把它们也抬上去。</summary>
    [Theory]
    [InlineData(nameof(DefaultFinanceCheckAdminController.TemplateSpecimen))]
    [InlineData(nameof(DefaultFinanceCheckAdminController.Calibration))]
    public void Placeholder_Only_Endpoints_Stay_Under_The_View_Gate(string action)
    {
        var method = typeof(DefaultFinanceCheckAdminController).GetMethod(action, BindingFlags.Public | BindingFlags.Instance);
        method.ShouldNotBeNull();

        method.GetCustomAttributes<ApiAuthorizeAttribute>(inherit: true)
            .Any(a => !string.IsNullOrEmpty(a.PermissionName))
            .ShouldBeFalse($"{action} never decrypts the stored account number and is read-only by design");
    }
}
