using System.Reflection;
using Tnzi.Finance.Banking.Controllers.Admin;
using Tnzi.Finance.Controllers.Admin;
using Tnzi.Security.Authorization;

namespace Tnzi.Finance.Tests;

/// <summary>
/// 会把单据过账进总账的端点，必须同时要 <c>finance.document.update</c>。
/// </summary>
/// <remarks>
/// 「过账 = .update」在全部单据控制器上一致（<c>{id}/post</c>、<c>{id}/void</c>、结算的 <c>apply</c>）。
/// 下面三个端点建单之后顺手过账（批量结算还核销），此前只挂 <c>.create</c>：被配置成只能起草的
/// 角色（view + create）经它们完成了在 post 端点上被拒绝的动作，唯一剩下的闸门是消费方自己的
/// 过账守卫。方法级的多个 <see cref="ApiAuthorizeAttribute"/> 是 AND 关系，所以断言挂在特性上。
/// </remarks>
public class PostingEndpointGateTests
{
    public static TheoryData<Type, string> PostingEndpoints => new()
    {
        { typeof(DefaultFinanceSettlementAdminController), nameof(DefaultFinanceSettlementAdminController.Pay) },
        { typeof(DefaultFinancePaymentEntryAdminController), nameof(DefaultFinancePaymentEntryAdminController.CreateFromExternal) },
        { typeof(DefaultFinanceBankFeedAdminController), nameof(DefaultFinanceBankFeedAdminController.CreateDocument) },
    };

    [Theory]
    [MemberData(nameof(PostingEndpoints))]
    public void Endpoints_That_Post_To_The_Ledger_Require_Both_Create_And_Update(Type controller, string action)
    {
        var method = controller.GetMethod(action, BindingFlags.Public | BindingFlags.Instance);
        method.ShouldNotBeNull();

        var codes = method.GetCustomAttributes<ApiAuthorizeAttribute>(inherit: true)
            .Select(a => a.PermissionName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        codes.ShouldContain("finance.document.create");
        codes.ShouldContain("finance.document.update",
            $"{controller.Name}.{action} posts to the ledger, which every document controller gates with finance.document.update");
    }

    /// <summary>反方向：纯起草的建单端点仍只要 <c>.create</c>，别顺手把整组都抬上去。</summary>
    [Fact]
    public void Plain_Draft_Creation_Stays_Under_The_Create_Code_Alone()
    {
        var method = typeof(DefaultFinancePaymentEntryAdminController)
            .GetMethod(nameof(DefaultFinancePaymentEntryAdminController.Create), BindingFlags.Public | BindingFlags.Instance);
        method.ShouldNotBeNull();

        method.GetCustomAttributes<ApiAuthorizeAttribute>(inherit: true)
            .Select(a => a.PermissionName)
            .ShouldNotContain("finance.document.update");
    }
}
