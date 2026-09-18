using System.Reflection;
using Tnzi.Identity.Controllers;
using Tnzi.Identity.Mvc;

namespace Tnzi.Identity.Tests;

/// <summary>
/// 自助端点里「后果重、且没有第二个人复核」的那几个，必须标 <c>[RequireStepUp]</c>。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ 断言的是<b>接线</b>，不是 step-up 服务本身写得对不对（那由
/// <c>StepUpServiceTests</c> 覆盖）。框架此前造好了 <c>[RequireStepUp]</c>、
/// 连 passkey 断言归属比对都做了，然后<b>一个端点都没标</b> ——
/// 于是拿到一枚被盗访问令牌的人第一件事就能把第二因子摘掉，而摘掉之后
/// 受害者连「重新登录时会被要求验证」这条兜底都没有了。
/// </para>
/// <para>
/// 这类「机制建好了没接上」的失效不会让任何测试变红，只能靠一条断言接线的门禁 ——
/// 与 <c>BuiltInLoginGuardTests</c> 存在的理由逐字相同。
/// </para>
/// <para>
/// ★ 未启用 <c>Identity:StepUp</c> 时该特性不拦任何请求，所以标注对既有部署零影响。
/// </para>
/// </remarks>
public class SensitiveSelfServiceEndpointTests
{
    public static TheoryData<string, string> ProtectedEndpoints => new()
    {
        // 拆除两步验证的四条
        { nameof(DefaultUserProfileController.DisableTwoFactor), StepUpScopes.TwoFactorManage },
        // 登记验证器的两条：setup 会重置密钥，enable 把一枚新密钥变成正式的第二因子 ——
        // 「换掉」第二因子与「摘掉」它后果同量级，判据是动作的后果而不是端点名里有没有 disable。
        { nameof(DefaultUserProfileController.GetTotpSetup), StepUpScopes.TwoFactorManage },
        { nameof(DefaultUserProfileController.EnableTotp), StepUpScopes.TwoFactorManage },
        { nameof(DefaultUserProfileController.SuspendTwoFactor), StepUpScopes.TwoFactorManage },
        { nameof(DefaultUserProfileController.DisableTotp), StepUpScopes.TwoFactorManage },
        { nameof(DefaultUserProfileController.DisableTwoFactorMethod), StepUpScopes.TwoFactorManage },
        // 销毁账户
        { nameof(DefaultUserProfileController.DeactivateAccount), StepUpScopes.AccountDestroy },
        { nameof(DefaultUserProfileController.DeleteAccount), StepUpScopes.AccountDestroy },
        // 换绑联系方式：验证码证明的是「新地址是真的」，不是「提出更换的人是账号主人」，
        // 而这两个字段正是账号的恢复路径。
        { nameof(DefaultUserProfileController.ConfirmChangeEmail), StepUpScopes.ContactChange },
        { nameof(DefaultUserProfileController.ConfirmChangePhone), StepUpScopes.ContactChange },
        // 新增一种登录方式：绑定的第三方身份在改密与撤销全部会话之后照样能登录，
        // 与「换掉第二因子」后果同量级 —— 判据仍是后果，不是端点名。
        { nameof(DefaultUserProfileController.IssueLinkToken), StepUpScopes.LoginMethodManage },
    };

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public void SensitiveEndpoint_RequiresStepUp(string methodName, string expectedScope)
    {
        var method = typeof(DefaultUserProfileController)
            .GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);

        method.ShouldNotBeNull($"端点 {methodName} 不存在了 —— 改名或删除时请一并更新本清单。");

        var attribute = method!.GetCustomAttribute<RequireStepUpAttribute>(inherit: true);
        attribute.ShouldNotBeNull($"{methodName} 缺少 [RequireStepUp]。");
        attribute!.Scope.ShouldBe(expectedScope);
    }

    /// <summary>
    /// 对照组：普通读端点<b>不</b>该有这个标注 —— 否则「哪些动作需要再证明一次」
    /// 就退化成「所有动作」，用户会被训练成无脑点确认。
    /// </summary>
    [Fact]
    public void OrdinaryEndpoint_DoesNotRequireStepUp()
    {
        var method = typeof(DefaultUserProfileController)
            .GetMethod(nameof(DefaultUserProfileController.GetCurrentUser), BindingFlags.Public | BindingFlags.Instance);

        method!.GetCustomAttribute<RequireStepUpAttribute>(inherit: true).ShouldBeNull();
    }

    /// <summary>范围名按动作分组：一次确认不该顺便把不相干的动作也放行。</summary>
    [Fact]
    public void Scopes_AreDistinctPerActionGroup()
    {
        var scopes = new[] { StepUpScopes.TwoFactorManage, StepUpScopes.AccountDestroy, StepUpScopes.ContactChange, StepUpScopes.LoginMethodManage };
        scopes.Distinct(StringComparer.Ordinal).Count().ShouldBe(scopes.Length);
    }
}
