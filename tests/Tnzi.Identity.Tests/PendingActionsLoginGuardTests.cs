namespace Tnzi.Identity.Tests;

/// <summary>
/// 框架内置守卫：账号已开好但本人还没接受邀请，即否决签发。
/// </summary>
/// <remarks>
/// ★ 这里只验守卫自身的裁决。「它有没有真的挂在各条签发路径上」由
/// <c>LoginGuardTests</c> 与 <c>AuthServiceTests</c> 覆盖，「它有没有被注册进容器」
/// 由 <c>Tnzi.Architecture.Tests</c> 的 <c>BuiltInLoginGuardTests</c> 覆盖 ——
/// 三者都需要，一个正确、但没被注册也没被调用的守卫，与没有这个守卫是同一回事。
/// </remarks>
public class PendingActionsLoginGuardTests
{
    [Fact]
    public async Task PendingAccount_IsDenied()
    {
        var guard = new PendingActionsLoginGuard();
        var user = UserWith(PendingUserActions.InvitationPending);

        var result = await guard.EvaluateAsync(Context(user));

        Assert.False(result.Allowed);
        Assert.Equal(403, result.Code);
        Assert.Equal(ErrorCodes.IDENTITY_ACTIVATION_PENDING, result.ErrorCode);
    }

    [Fact]
    public async Task AcceptedAccount_IsAllowed()
    {
        var guard = new PendingActionsLoginGuard();
        var user = UserWith(PendingUserActions.None);

        var result = await guard.EvaluateAsync(Context(user));

        Assert.True(result.Allowed);
    }

    /// <summary>
    /// ★★ 这条是本守卫存在的全部理由：验证码登录会按邮箱找到预建账号、
    /// 走「用户已存在」分支然后照常签发。守卫必须对**每一条**签发方式都拒绝，
    /// 而不只是密码登录那一条。
    /// </summary>
    [Theory]
    [InlineData(LoginMethod.Password)]
    [InlineData(LoginMethod.VerificationCode)]
    [InlineData(LoginMethod.OAuth)]
    [InlineData(LoginMethod.Passkey)]
    [InlineData(LoginMethod.RefreshToken)]
    [InlineData(LoginMethod.TwoFactor)]
    public async Task PendingAccount_IsDenied_OnEverySignInMethod(LoginMethod method)
    {
        var guard = new PendingActionsLoginGuard();
        var user = UserWith(PendingUserActions.InvitationPending);

        var result = await guard.EvaluateAsync(new LoginGuardContext(user, method, "127.0.0.1", "test-agent"));

        Assert.False(result.Allowed);
    }

    /// <summary>
    /// ★ 如实告知而不是伪装成「用户名或密码错误」：本人需要知道该去点邀请链接。
    /// 含糊其辞只会让他反复去重置密码，而那条路同样被挡着。
    /// </summary>
    [Fact]
    public async Task Denial_TellsTheTruth_SoTheUserKnowsToUseTheInvitationLink()
    {
        var guard = new PendingActionsLoginGuard();
        var user = UserWith(PendingUserActions.InvitationPending);

        var result = await guard.EvaluateAsync(Context(user));

        Assert.NotEqual("Invalid username or password", result.Message);
        Assert.NotEqual(ErrorCodes.IDENTITY_INVALID_PASSWORD, result.ErrorCode);
        Assert.Contains("invitation", result.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(LoginMethod.Passkey), result.AuditReason, StringComparison.Ordinal);
    }

    /// <summary>
    /// 排在消费方守卫之前，与锁定守卫同一层。
    /// </summary>
    [Fact]
    public void Guard_RunsBeforeAnyConsumerGuard()
    {
        Assert.True(new PendingActionsLoginGuard().Order < 0);
    }

    private static LoginGuardContext Context(User user)
        => new(user, LoginMethod.Passkey, "127.0.0.1", "test-agent");

    private static User UserWith(PendingUserActions state)
        => new() { Id = Guid.NewGuid(), UserName = "invited", PendingActions = state };
}
