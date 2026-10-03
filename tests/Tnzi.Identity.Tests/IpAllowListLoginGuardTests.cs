using Tnzi.MultiTenancy;
using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

/// <summary>
/// 框架内置守卫：账号开了登录 IP 允许列表时，只从列表内地址签发令牌。
/// </summary>
/// <remarks>
/// 这里只验守卫自身的裁决与它的读表方式。「它有没有挂在守卫链上」由模块注册测试把守：
/// 一个正确但没被注册的守卫，与没有这个守卫是同一回事。
/// </remarks>
public class IpAllowListLoginGuardTests
{
    private static readonly string[] NoRoles = [];
    private static readonly string[] NoExemptions = [];

    private static UserSignInPolicy Policy(bool enabled, string? ips) => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        IpAllowListEnabled = enabled,
        AllowedIps = ips
    };

    [Fact]
    public void Decide_NoPolicyRow_Allows()
    {
        var result = IpAllowListLoginGuard.Decide(null, "203.0.113.5", NoRoles, NoExemptions);

        Assert.True(result.Allowed);
    }

    [Fact]
    public void Decide_PolicyRowWithListSwitchedOff_AllowsWhateverTheListSays()
    {
        var result = IpAllowListLoginGuard.Decide(Policy(false, "10.0.0.1"), "203.0.113.5", NoRoles, NoExemptions);

        Assert.True(result.Allowed);
    }

    [Fact]
    public void Decide_ClientOnTheList_Allows()
    {
        var result = IpAllowListLoginGuard.Decide(Policy(true, "10.0.0.0/8\n203.0.113.5"), "203.0.113.5", NoRoles, NoExemptions);

        Assert.True(result.Allowed);
    }

    /// <summary>
    /// 拒绝必须与「用户名或密码错误」逐字同形：守卫在密码校验之后运行，任何可区分的回答都在证明密码是对的。
    /// 真实原因只进审计。
    /// </summary>
    [Fact]
    public void Decide_ClientOffTheList_DeniesAsInvalidCredentials()
    {
        var result = IpAllowListLoginGuard.Decide(Policy(true, "10.0.0.0/8"), "203.0.113.5", NoRoles, NoExemptions);

        var reference = LoginGuardResult.DenyAsInvalidCredentials("reference");
        Assert.False(result.Allowed);
        Assert.Equal(reference.Message, result.Message);
        Assert.Equal(reference.Code, result.Code);
        Assert.Equal(reference.ErrorCode, result.ErrorCode);
        Assert.Contains("203.0.113.5", result.AuditReason);
        Assert.DoesNotContain("203.0.113.5", result.Message);
    }

    /// <summary>
    /// 刷新路径例外：调用方已经持有有效的刷新令牌，拒绝理由证明不了任何它不知道的东西，
    /// 所以如实说「当前网络不在允许列表里」，而不是把人送回登录页对着正确的密码反复重试。
    /// </summary>
    [Fact]
    public void Decide_ClientOffTheList_OnRefresh_SaysWhyWithTheDedicatedCode()
    {
        var result = IpAllowListLoginGuard.Decide(
            Policy(true, "10.0.0.0/8"), "203.0.113.5", NoRoles, NoExemptions, LoginMethod.RefreshToken);

        Assert.False(result.Allowed);
        Assert.Equal(403, result.Code);
        Assert.Equal(ErrorCodes.IDENTITY_SIGN_IN_IP_NOT_ALLOWED, result.ErrorCode);
        Assert.Equal(IpAllowListLoginGuard.RefreshDeniedMessage, result.Message);
        Assert.Contains("203.0.113.5", result.AuditReason);
    }

    /// <summary>
    /// 除刷新外的每一条签发路径都在凭据校验之后跑守卫，必须仍与密码错误同形。
    /// </summary>
    [Theory]
    [InlineData(LoginMethod.Password)]
    [InlineData(LoginMethod.PasswordWithRefreshToken)]
    [InlineData(LoginMethod.VerificationCode)]
    [InlineData(LoginMethod.TwoFactor)]
    [InlineData(LoginMethod.OAuth)]
    [InlineData(LoginMethod.Registration)]
    [InlineData(LoginMethod.Passkey)]
    [InlineData(LoginMethod.Invitation)]
    public void Decide_ClientOffTheList_OnEveryOtherMethod_StaysIndistinguishableFromAWrongPassword(LoginMethod method)
    {
        var result = IpAllowListLoginGuard.Decide(Policy(true, "10.0.0.0/8"), "203.0.113.5", NoRoles, NoExemptions, method);

        var reference = LoginGuardResult.DenyAsInvalidCredentials("reference");
        Assert.False(result.Allowed);
        Assert.Equal(reference.Message, result.Message);
        Assert.Equal(reference.Code, result.Code);
        Assert.Equal(reference.ErrorCode, result.ErrorCode);
    }

    [Fact]
    public void Decide_UnknownClientAddress_IsDeniedAndSaysSoInTheAuditReason()
    {
        var result = IpAllowListLoginGuard.Decide(Policy(true, "10.0.0.0/8"), null, NoRoles, NoExemptions);

        Assert.False(result.Allowed);
        Assert.Contains("unknown address", result.AuditReason);
    }

    /// <summary>
    /// 开着开关、框里没有：不限制任何人。写入路径拒绝存这种行；这里是给绕过写入路径的行留的安全网。
    /// 被一个空列表拒之门外的第一个人，就是来修它的那个人。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("# nothing but a comment")]
    public void Decide_EnabledWithEmptyList_Allows(string? ips)
    {
        var result = IpAllowListLoginGuard.Decide(Policy(true, ips), "203.0.113.5", NoRoles, NoExemptions);

        Assert.True(result.Allowed);
    }

    [Fact]
    public void Decide_ExemptRole_AllowsRegardlessOfList_CaseInsensitively()
    {
        var result = IpAllowListLoginGuard.Decide(
            Policy(true, "10.0.0.0/8"), "203.0.113.5", ["Coordinator", "SUPERADMIN"], ["superadmin"]);

        Assert.True(result.Allowed);
    }

    [Fact]
    public void Decide_RoleNotInExemptions_StillEnforced()
    {
        var result = IpAllowListLoginGuard.Decide(
            Policy(true, "10.0.0.0/8"), "203.0.113.5", ["Coordinator"], ["SuperAdmin"]);

        Assert.False(result.Allowed);
    }

    [Fact]
    public void Order_RunsAfterTheAccountStateGuards_AndBeforeCustomGuards()
    {
        var (guard, _, _) = CreateGuard(policy: null, roles: [], exempt: []);

        // 账号锁定 / 待办阻断两条守卫占着 int.MinValue 与 int.MinValue + 1；自定义守卫默认 0。
        Assert.True(guard.Order > int.MinValue + 1);
        Assert.True(guard.Order < 0);
    }

    [Fact]
    public async Task EvaluateAsync_ReadsTheSigningInUsersRow_AndDeniesOffListClient()
    {
        var user = new User { Id = Guid.NewGuid(), UserName = "locked-down" };
        var policy = new UserSignInPolicy { Id = Guid.NewGuid(), UserId = user.Id, IpAllowListEnabled = true, AllowedIps = "10.0.0.0/8" };
        var (guard, _, _) = CreateGuard(policy, roles: [], exempt: []);

        var denied = await guard.EvaluateAsync(new LoginGuardContext(user, LoginMethod.Password, "203.0.113.5", null));
        var allowed = await guard.EvaluateAsync(new LoginGuardContext(user, LoginMethod.RefreshToken, "10.1.2.3", null));

        Assert.False(denied.Allowed);
        Assert.True(allowed.Allowed);
    }

    [Fact]
    public async Task EvaluateAsync_PassesTheLoginMethodThrough_SoARefreshOffTheListGetsTheDedicatedCode()
    {
        var user = new User { Id = Guid.NewGuid(), UserName = "locked-down" };
        var policy = new UserSignInPolicy { Id = Guid.NewGuid(), UserId = user.Id, IpAllowListEnabled = true, AllowedIps = "10.0.0.0/8" };
        var (guard, _, _) = CreateGuard(policy, roles: [], exempt: []);

        var refresh = await guard.EvaluateAsync(new LoginGuardContext(user, LoginMethod.RefreshToken, "203.0.113.5", null));
        var password = await guard.EvaluateAsync(new LoginGuardContext(user, LoginMethod.Password, "203.0.113.5", null));

        Assert.Equal(ErrorCodes.IDENTITY_SIGN_IN_IP_NOT_ALLOWED, refresh.ErrorCode);
        Assert.Equal(InvalidCredentialsResponse.ErrorCode, password.ErrorCode);
        Assert.Equal(InvalidCredentialsResponse.Message, password.Message);
    }

    /// <summary>
    /// 只有真的开了限制才查角色：豁免是例外路径，不该让每次登录都多一次角色查询。
    /// </summary>
    [Fact]
    public async Task EvaluateAsync_WithoutAnEnabledPolicy_NeverAsksForRoles()
    {
        var user = new User { Id = Guid.NewGuid(), UserName = "plain" };
        var (guard, userManager, _) = CreateGuard(policy: null, roles: [], exempt: ["SuperAdmin"]);

        var result = await guard.EvaluateAsync(new LoginGuardContext(user, LoginMethod.Password, "203.0.113.5", null));

        Assert.True(result.Allowed);
        userManager.Verify(m => m.GetRolesAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task EvaluateAsync_WithExemptRoleConfigured_ConsultsRolesAndExempts()
    {
        var user = new User { Id = Guid.NewGuid(), UserName = "root" };
        var policy = new UserSignInPolicy { Id = Guid.NewGuid(), UserId = user.Id, IpAllowListEnabled = true, AllowedIps = "10.0.0.0/8" };
        var (guard, _, _) = CreateGuard(policy, roles: ["SuperAdmin"], exempt: ["SuperAdmin"]);

        var result = await guard.EvaluateAsync(new LoginGuardContext(user, LoginMethod.Password, "203.0.113.5", null));

        Assert.True(result.Allowed);
    }

    /// <summary>
    /// 角色是多租户实体。匿名登录请求的当前租户未必是账号的租户：不切过去就读到空角色集，豁免落空，
    /// 来恢复账号的人被拒在门外。
    /// </summary>
    [Fact]
    public async Task EvaluateAsync_ReadsRolesInTheUsersOwnTenant()
    {
        var tenantId = Guid.NewGuid();
        var user = new User { Id = Guid.NewGuid(), UserName = "root", TenantId = tenantId };
        var policy = new UserSignInPolicy { Id = Guid.NewGuid(), UserId = user.Id, IpAllowListEnabled = true, AllowedIps = "10.0.0.0/8" };

        Guid? ambient = null;
        var currentTenant = new Mock<ICurrentTenant>();
        currentTenant.SetupGet(t => t.Id).Returns(() => ambient);
        currentTenant.Setup(t => t.Change(It.IsAny<Guid?>(), It.IsAny<string?>()))
            .Returns((Guid? id, string? _) =>
            {
                var previous = ambient;
                ambient = id;
                return new DisposeAction(() => ambient = previous);
            });

        var (guard, userManager, _) = CreateGuard(policy, roles: [], exempt: ["SuperAdmin"], currentTenant.Object);
        // 只有在账号自己的租户下才读得到角色，模拟严格等值的租户过滤器。
        userManager.Setup(m => m.GetRolesAsync(user))
            .ReturnsAsync(() => ambient == tenantId ? ["SuperAdmin"] : []);

        var result = await guard.EvaluateAsync(new LoginGuardContext(user, LoginMethod.Password, "203.0.113.5", null));

        Assert.True(result.Allowed);
        Assert.Null(ambient);
    }

    private sealed class DisposeAction(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }

    private static (IpAllowListLoginGuard Guard, Mock<UserManager<User>> UserManager, Mock<IRepository<UserSignInPolicy, Guid>> Repository)
        CreateGuard(UserSignInPolicy? policy, string[] roles, string[] exempt, ICurrentTenant? currentTenant = null)
    {
        var rows = policy == null ? new List<UserSignInPolicy>() : [policy];
        var repository = new Mock<IRepository<UserSignInPolicy, Guid>>();
        repository.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(() => rows.BuildMock());

        var store = new Mock<IUserStore<User>>();
        var userManager = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        userManager.Setup(m => m.GetRolesAsync(It.IsAny<User>())).ReturnsAsync(roles.ToList());

        var options = Monitor(new IdentityOptions
        {
            AccountSecurity = new AccountSecurityOptions { IpAllowListExemptRoles = exempt }
        });

        return (new IpAllowListLoginGuard(repository.Object, userManager.Object, options, currentTenant), userManager, repository);
    }

    private static IOptionsMonitor<IdentityOptions> Monitor(IdentityOptions options)
    {
        var monitor = new Mock<IOptionsMonitor<IdentityOptions>>();
        monitor.SetupGet(m => m.CurrentValue).Returns(options);
        return monitor.Object;
    }
}
