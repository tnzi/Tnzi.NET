using Tnzi.Identity;
using Tnzi.Identity.Services;

namespace Tnzi.Architecture.Tests;

/// <summary>
/// 架构门禁：账号锁定 / 停用的判定必须以<b>框架内置守卫</b>的形式挂在守卫链上。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要门禁。</b>这道检查在密码登录上是
/// <c>SignInManager.CheckPasswordSignInAsync</c> 的 <c>PreSignInCheck</c> <b>顺手</b>做掉的 ——
/// 它绑在「校验密码」这个动作上，而不是绑在「签发令牌」上。于是每引入一条
/// <b>凭据校验不在 SignInManager 里</b>的登录方式（验证码登录、OAuth、passkey），
/// 它就跟着消失一次，<b>而且不会有任何东西报错</b>：被停用的账号照常拿到完整令牌。
/// 2026-08-16 实测到两条这样的路径（passkey 与验证码登录）。
/// </para>
/// <para>
/// ★ <b>本门禁查的是「注册」，不是「守卫写得对不对」。</b>后者由
/// <c>LockedAccountLoginGuardTests</c>（裁决逻辑）与 <c>AuthServiceTests</c>（各条签发路径
/// 真的被拦下）覆盖，而那两处都是<b>手工装配</b>守卫的 —— 它们全绿也证明不了
/// <c>IdentityModule</c> 真的把它注册进了容器。少了这一条，删掉那行注册代码
/// 不会让任何测试变红。
/// </para>
/// <para>
/// <b>为什么落在本项目。</b>判定要看真实的全模块图（<see cref="AllModulesStartupModule"/>
/// 跑完三个配置阶段之后的服务归属），单个模块的测试项目给不出这个。
/// </para>
/// </remarks>
public class BuiltInLoginGuardTests
{
    [Fact]
    public void IdentityModule_RegistersTheLockedAccountGuard()
    {
        var graph = ArchitectureModuleGraph.Load();
        AssertFixtureIsSound(graph);

        Assert.True(
            graph.ServiceMap.TryGetValue(typeof(IdentityModule), out var identityServices),
            "IdentityModule registered no services at all - the fixture, not the guard, is what broke.");

        var registered = identityServices!.Any(d =>
            d.ServiceType == typeof(ILoginGuard)
            && d.ImplementationType == typeof(LockedAccountLoginGuard));

        Assert.True(registered,
            "IdentityModule no longer registers LockedAccountLoginGuard. Without it the login guard chain "
            + "is empty on a default deployment, and every sign-in path whose credential check happens "
            + "outside SignInManager (verification-code login, OAuth, passkey) stops rejecting "
            + "locked-out / disabled accounts - silently.");
    }

    /// <summary>
    /// 邀请未接受的判定同样必须是注册进容器的内置守卫。
    /// </summary>
    /// <remarks>
    /// ★ 它<b>不能</b>由上面那条锁定守卫代劳：邀请创建的账号确实同时被置上了锁定，
    /// 但「启用」与「解锁」本来就是清掉 <c>LockoutEnd</c> ——
    /// 管理员对一个未接受邀请的账号点一下「启用」，锁定守卫就如其所愿地放行了，
    /// 而那个账号没有密码、没有二次验证、角色却已按管理员的意思预设好。
    /// 少了这行注册，验证码登录只需要收到一封邮件就能带着预设角色进来。
    /// </remarks>
    [Fact]
    public void IdentityModule_RegistersThePendingActivationGuard()
    {
        var graph = ArchitectureModuleGraph.Load();
        AssertFixtureIsSound(graph);

        Assert.True(
            graph.ServiceMap.TryGetValue(typeof(IdentityModule), out var identityServices),
            "IdentityModule registered no services at all - the fixture, not the guard, is what broke.");

        var registered = identityServices!.Any(d =>
            d.ServiceType == typeof(ILoginGuard)
            && d.ImplementationType == typeof(PendingActionsLoginGuard));

        Assert.True(registered,
            "IdentityModule no longer registers PendingActionsLoginGuard. Without it an invited-but-not-yet-"
            + "accepted account - which has no password and no second factor, but does have its roles already "
            + "assigned - can be signed in through any path that does not go through SignInManager. "
            + "Verification-code login is the shortest one: receiving a single email is enough.");
    }

    // 「排在消费方守卫之前」那条断言在 Tnzi.Identity.Tests 里（那边能方便地构造 UserManager），
    // 这里只管注册这一件事 —— 本项目的价值是真实全模块图，不是重复单模块能做的事。

    private static void AssertFixtureIsSound(ModuleLoadResult graph)
    {
        if (graph.Failures.Count > 0)
        {
            Assert.Fail(
                $"{graph.Failures.Count} module(s) failed to configure, so this gate cannot tell "
                + $"'not registered' from 'not scanned':{Environment.NewLine}"
                + string.Join(Environment.NewLine, graph.Failures.Select(f => $"  - {f}")));
        }
    }
}
