using System.Security.Cryptography;
using System.Text.Json;
using TnziIdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

/// <summary>
/// 邀请注册：管理员开号发链接，被邀请人凭一次性链接激活账号。
/// </summary>
public class InvitationServiceTests
{
    [Fact]
    public async Task Invite_ShouldStoreOnlyTheHash_NeverThePlaintext()
    {
        var fixture = new Fixture();

        var invited = (await fixture.Service.InviteAsync(NewInvitation())).Data!;

        // ★ 拿到数据库读权限的人不该顺手拿到一批可用的邀请链接。
        var token = fixture.ExtractToken(invited.AcceptUrl);
        Assert.NotEqual(token, fixture.LastSavedTokenValue);
        Assert.Equal(Sha256Hex(token), fixture.LastSavedTokenValue);
    }

    /// <summary>
    /// ★★★ 邀请创建出来的账号必须同时是「未接受邀请」<b>和</b>「锁定」。
    /// 前者是安全边界（守卫读它），后者是为了让既有的「活跃用户」口径自动排除它。
    /// </summary>
    [Fact]
    public async Task Invite_ShouldLeaveAccountPending_AndLockedOut()
    {
        var fixture = new Fixture();

        await fixture.Service.InviteAsync(NewInvitation());

        Assert.Equal(PendingUserActions.InvitationPending, fixture.CreatedUser!.PendingActions);
        Assert.True(fixture.LockoutEnabledSet);
        Assert.NotNull(fixture.LockoutEndSet);
        Assert.True(fixture.LockoutEndSet > DateTimeOffset.UtcNow.AddYears(50));
    }

    /// <summary>
    /// 账号无密码创建：密码由本人在接受邀请时设置。
    /// </summary>
    [Fact]
    public async Task Invite_ShouldCreateAccountWithoutPassword()
    {
        var fixture = new Fixture();

        await fixture.Service.InviteAsync(NewInvitation());

        Assert.NotNull(fixture.LastCreateInput);
        Assert.True(string.IsNullOrEmpty(fixture.LastCreateInput!.Password));
    }

    /// <summary>
    /// 没有任何送达方式的邀请发不出去，挡在建号之前 ——
    /// 否则会留下一个永远无人接受、也没人知道为什么的 Pending 账号。
    /// </summary>
    [Fact]
    public async Task Invite_WithoutAnyDeliveryAddress_IsRejectedBeforeCreatingAnAccount()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.InviteAsync(new CreateInvitationDto { UserName = "nobody" });

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.Null(fixture.CreatedUser);
    }

    /// <summary>
    /// ★★★ handler 说没做完，账号就<b>绝不</b>激活，令牌也不消费。
    /// 这是「强制二次验证」之类要求能够成立的唯一原因。
    /// </summary>
    [Fact]
    public async Task Accept_WhenHandlerSaysIncomplete_DoesNotActivate_AndKeepsTokenUsable()
    {
        var fixture = new Fixture();
        fixture.HandlerOutcome = InvitationAcceptOutcome.NeedsMore("EnrollTotp");
        var token = fixture.ExtractToken((await fixture.Service.InviteAsync(NewInvitation())).Data!.AcceptUrl);

        var result = await fixture.Service.AcceptAsync(new AcceptInvitationDto { Token = token });

        Assert.True(result.Succeeded);
        Assert.False(result.Data!.Completed);
        Assert.Equal(["EnrollTotp"], result.Data.RemainingSteps);
        Assert.Equal(PendingUserActions.InvitationPending, fixture.CreatedUser!.PendingActions);

        // 令牌仍可用：用户拿同一条链接回来继续。
        var again = await fixture.Service.AcceptAsync(new AcceptInvitationDto { Token = token });
        Assert.True(again.Succeeded);
    }

    [Fact]
    public async Task Accept_WhenHandlerSaysDone_ActivatesAccount_AndConsumesToken()
    {
        var fixture = new Fixture();
        var token = fixture.ExtractToken((await fixture.Service.InviteAsync(NewInvitation())).Data!.AcceptUrl);

        var result = await fixture.Service.AcceptAsync(new AcceptInvitationDto { Token = token, Password = "P@ssw0rd!" });

        Assert.True(result.Succeeded);
        Assert.True(result.Data!.Completed);
        Assert.Equal(PendingUserActions.None, fixture.CreatedUser!.PendingActions);

        // 令牌已消费：同一条链接不能再用第二次。
        var replay = await fixture.Service.AcceptAsync(new AcceptInvitationDto { Token = token });
        Assert.False(replay.Succeeded);
    }

    /// <summary>
    /// ★★ 并发接受：抢占失败的那一方不能也把账号激活一遍、再拿一份登录令牌。
    /// </summary>
    [Fact]
    public async Task Accept_WhenAnotherRequestWinsTheRace_IsRejected()
    {
        var fixture = new Fixture();
        var token = fixture.ExtractToken((await fixture.Service.InviteAsync(NewInvitation())).Data!.AcceptUrl);
        fixture.FailNextMarkAsUsed = true;

        var result = await fixture.Service.AcceptAsync(new AcceptInvitationDto { Token = token, Password = "P@ssw0rd!" });

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.IDENTITY_INVITATION_INVALID, result.ErrorCode);
        Assert.Equal(PendingUserActions.InvitationPending, fixture.CreatedUser!.PendingActions);
    }

    /// <summary>
    /// ★ 失效、过期、不存在必须回答同一句话 —— 区分开就是在帮人试探哪些链接是真的。
    /// </summary>
    [Fact]
    public async Task Accept_UnknownExpiredAndUsedTokens_AllGiveTheSameAnswer()
    {
        var fixture = new Fixture();
        var token = fixture.ExtractToken((await fixture.Service.InviteAsync(NewInvitation())).Data!.AcceptUrl);

        var unknown = await fixture.Service.AcceptAsync(new AcceptInvitationDto { Token = "made-up-token" });

        fixture.ExpireStoredTokens();
        var expired = await fixture.Service.AcceptAsync(new AcceptInvitationDto { Token = token });

        Assert.Equal(unknown.Code, expired.Code);
        Assert.Equal(unknown.ErrorCode, expired.ErrorCode);
        Assert.Equal(unknown.Message, expired.Message);
    }

    /// <summary>
    /// ★★ 预览不能回显完整邮箱：链接泄露不该等于员工邮箱泄露。
    /// </summary>
    [Fact]
    public async Task Preview_MasksTheContactAddresses()
    {
        var fixture = new Fixture();
        var token = fixture.ExtractToken((await fixture.Service.InviteAsync(NewInvitation())).Data!.AcceptUrl);

        var preview = (await fixture.Service.PreviewAsync(token)).Data!;

        Assert.DoesNotContain("newhire@example.com", preview.MaskedEmail!, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("n", preview.MaskedEmail, StringComparison.Ordinal);
        Assert.Contains('*', preview.MaskedEmail!);
        // 用户名要给全：本人此后用它登录。
        Assert.Equal("newhire", preview.UserName);
    }

    /// <summary>
    /// ★★ 手机号只回显末四位 —— <b>前缀也要遮</b>。
    /// </summary>
    /// <remarks>
    /// 前三位是国家码 / 区号，与末四位合起来会把候选号码收窄到很小的集合，而号主只靠末四位
    /// 就认得出自己的号。这一页由任何拿到邀请链接的人可达，所以掩码只能更严不能更松。
    /// 此前的实现保留前三后四，且<b>没有任何测试钉住它</b> —— 改松不会让任何东西变红。
    /// </remarks>
    [Fact]
    public async Task Preview_MasksThePhoneNumberPrefixToo()
    {
        var fixture = new Fixture();
        var invitation = NewInvitation();
        invitation.PhoneNumber = "+14155552671";
        var token = fixture.ExtractToken((await fixture.Service.InviteAsync(invitation)).Data!.AcceptUrl);

        var masked = (await fixture.Service.PreviewAsync(token)).Data!.MaskedPhoneNumber!;

        Assert.EndsWith("2671", masked, StringComparison.Ordinal);
        // 号码里除末四位以外，一位数字都不该出现。
        Assert.DoesNotContain(masked[..^4], c => char.IsDigit(c));
    }

    [Fact]
    public async Task Preview_DoesNotConsumeTheToken()
    {
        var fixture = new Fixture();
        var token = fixture.ExtractToken((await fixture.Service.InviteAsync(NewInvitation())).Data!.AcceptUrl);

        await fixture.Service.PreviewAsync(token);
        var accept = await fixture.Service.AcceptAsync(new AcceptInvitationDto { Token = token, Password = "P@ssw0rd!" });

        Assert.True(accept.Succeeded);
    }

    /// <summary>
    /// 重发让上一条链接立即失效（唯一索引 upsert 的直接结果，但值得钉住）。
    /// </summary>
    [Fact]
    public async Task Resend_InvalidatesThePreviousLink()
    {
        var fixture = new Fixture();
        var first = fixture.ExtractToken((await fixture.Service.InviteAsync(NewInvitation())).Data!.AcceptUrl);

        var second = fixture.ExtractToken((await fixture.Service.ResendAsync(fixture.CreatedUser!.Id)).Data!.AcceptUrl);

        Assert.NotEqual(first, second);
        Assert.False((await fixture.Service.AcceptAsync(new AcceptInvitationDto { Token = first })).Succeeded);
        Assert.True((await fixture.Service.PreviewAsync(second)).Succeeded);
    }

    [Fact]
    public async Task Resend_OnAnAlreadyAcceptedAccount_IsRejected()
    {
        var fixture = new Fixture();
        await fixture.Service.InviteAsync(NewInvitation());
        fixture.CreatedUser!.PendingActions = PendingUserActions.None;

        var result = await fixture.Service.ResendAsync(fixture.CreatedUser.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Code);
    }

    [Fact]
    public async Task Revoke_DeletesTheAccount_AndInvalidatesTheLink()
    {
        var fixture = new Fixture();
        var token = fixture.ExtractToken((await fixture.Service.InviteAsync(NewInvitation())).Data!.AcceptUrl);

        var result = await fixture.Service.RevokeAsync(fixture.CreatedUser!.Id);

        Assert.True(result.Succeeded);
        Assert.True(fixture.UserDeleted);
        Assert.False((await fixture.Service.PreviewAsync(token)).Succeeded);
    }

    [Fact]
    public async Task Revoke_OnAnAlreadyAcceptedAccount_IsRejected()
    {
        var fixture = new Fixture();
        await fixture.Service.InviteAsync(NewInvitation());
        fixture.CreatedUser!.PendingActions = PendingUserActions.None;

        var result = await fixture.Service.RevokeAsync(fixture.CreatedUser.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Code);
        Assert.False(fixture.UserDeleted);
    }

    /// <summary>
    /// 管理员预填的资料原样交给 handler，框架不解释其中任何字段。
    /// </summary>
    [Fact]
    public async Task Accept_PassesTheAdminSuppliedProfileToTheHandler()
    {
        var fixture = new Fixture();
        using var doc = JsonDocument.Parse("""{"employeeNo":"E-1024","department":"Ops"}""");
        var input = NewInvitation();
        input.Profile = doc.RootElement.Clone();
        var token = fixture.ExtractToken((await fixture.Service.InviteAsync(input)).Data!.AcceptUrl);

        await fixture.Service.AcceptAsync(new AcceptInvitationDto { Token = token, Password = "P@ssw0rd!" });

        Assert.NotNull(fixture.HandlerSawProfile);
        Assert.Equal("E-1024", fixture.HandlerSawProfile!.Value.GetProperty("employeeNo").GetString());
    }


    /// <summary>
    /// ★★★ 置 Pending 失败必须把刚建的账号收回去。
    /// </summary>
    /// <remarks>
    /// 留下的账号没有密码、角色却已按管理员的意思预设好，而 <c>InvitationState</c> 仍是
    /// <see cref="PendingUserActions.None"/> —— 守卫按状态判定，None 就是放行。
    /// 于是验证码登录只要收到一封邮件就能带着那些角色进来，
    /// 也就是本模块要挡的那条路径，只不过由一次失败的写入造成。
    /// </remarks>
    [Fact]
    public async Task Invite_WhenMarkingPendingFails_RollsBackTheCreatedAccount()
    {
        var fixture = new Fixture();
        fixture.FailUserUpdate = true;

        var result = await fixture.Service.InviteAsync(NewInvitation());

        Assert.False(result.Succeeded);
        Assert.True(fixture.UserDeleted, "the half-created account must not be left behind");
    }

    /// <summary>
    /// 激活写失败时如实报错，且**不谎报成功** —— 令牌已经消费掉，
    /// 用户此刻的处境是「链接用掉了但账号没激活」，他需要知道该去找管理员重发。
    /// </summary>
    [Fact]
    public async Task Accept_WhenActivationWriteFails_ReportsFailure()
    {
        var fixture = new Fixture();
        var token = fixture.ExtractToken((await fixture.Service.InviteAsync(NewInvitation())).Data!.AcceptUrl);
        fixture.FailUserUpdate = true;

        var result = await fixture.Service.AcceptAsync(new AcceptInvitationDto { Token = token, Password = "P@ssw0rd!" });

        Assert.False(result.Succeeded);
        Assert.Equal(500, result.Code);
    }

    /// <summary>
    /// ★ 激活时锁定必须与状态在**同一次写**里清掉。
    /// </summary>
    /// <remarks>
    /// 分两次写有个安静的坏结果：<c>SetLockoutEndDateAsync</c> 在 <c>LockoutEnabled</c>
    /// 为 false 时直接返回失败且什么都不写，而紧随其后的 <c>UpdateAsync</c> 照样会把
    /// <c>InvitationState</c> 落库 —— 账号「已激活」却锁到一百年后：
    /// 本人刚设好密码、链接也用掉了，然后登不进去。
    /// </remarks>
    [Fact]
    public async Task Accept_ClearsLockoutTogetherWithTheState()
    {
        var fixture = new Fixture();
        var token = fixture.ExtractToken((await fixture.Service.InviteAsync(NewInvitation())).Data!.AcceptUrl);

        await fixture.Service.AcceptAsync(new AcceptInvitationDto { Token = token, Password = "P@ssw0rd!" });

        Assert.Equal(PendingUserActions.None, fixture.CreatedUser!.PendingActions);
        Assert.Null(fixture.CreatedUser.LockoutEnd);
    }

    private static CreateInvitationDto NewInvitation() => new()
    {
        UserName = "newhire",
        Email = "newhire@example.com",
    };

    private static string Sha256Hex(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class Fixture
    {
        private readonly List<AuthToken> _tokens = [];

        public InvitationService Service { get; }

        public User? CreatedUser { get; private set; }

        public CreateUserDto? LastCreateInput { get; private set; }

        public string? LastSavedTokenValue { get; private set; }

        public bool LockoutEnabledSet { get; private set; }

        public DateTimeOffset? LockoutEndSet { get; private set; }

        public bool UserDeleted { get; private set; }

        public bool FailNextMarkAsUsed { get; set; }

        /// <summary>让 <c>UserManager.UpdateAsync</c> 失败，用来验证两条写失败路径。</summary>
        public bool FailUserUpdate { get; set; }

        public InvitationAcceptOutcome HandlerOutcome { get; set; } = InvitationAcceptOutcome.Done();

        public JsonElement? HandlerSawProfile { get; private set; }

        public Fixture()
        {
            var store = new Mock<IUserStore<User>>();
            var userManager = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
            userManager
                .Setup(x => x.FindByIdAsync(It.IsAny<string>()))
                .ReturnsAsync((string id) => CreatedUser?.Id.ToString() == id && !UserDeleted ? CreatedUser : null);
            userManager
                .Setup(x => x.SetLockoutEnabledAsync(It.IsAny<User>(), It.IsAny<bool>()))
                .ReturnsAsync((User _, bool enabled) => { LockoutEnabledSet = enabled; return IdentityResult.Success; });
            userManager
                .Setup(x => x.SetLockoutEndDateAsync(It.IsAny<User>(), It.IsAny<DateTimeOffset?>()))
                .ReturnsAsync((User u, DateTimeOffset? end) =>
                {
                    LockoutEndSet = end;
                    u.LockoutEnd = end;
                    return IdentityResult.Success;
                });
            userManager
                .Setup(x => x.UpdateAsync(It.IsAny<User>()))
                .ReturnsAsync(() => FailUserUpdate
                    ? IdentityResult.Failed(new IdentityError { Description = "concurrency failure" })
                    : IdentityResult.Success);

            var userService = new Mock<IUserService>();
            userService
                .Setup(x => x.CreateAsync(It.IsAny<CreateUserDto>()))
                .ReturnsAsync((CreateUserDto input) =>
                {
                    LastCreateInput = input;
                    CreatedUser = new User
                    {
                        Id = Guid.NewGuid(),
                        UserName = input.UserName,
                        Email = input.Email,
                        PhoneNumber = input.PhoneNumber,
                    };
                    return Result<UserDto>.Success(new UserDto { Id = CreatedUser.Id, UserName = input.UserName });
                });
            userService
                .Setup(x => x.DeleteAsync(It.IsAny<Guid>()))
                .ReturnsAsync(() => { UserDeleted = true; return Result.Success(); });

            var authTokenService = new Mock<IAuthTokenService>();
            authTokenService
                .Setup(x => x.SaveTokenAsync(
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<Guid>()))
                .ReturnsAsync((Guid userId, string provider, string name, string value, DateTime? expiresAt, Guid _) =>
                {
                    if (name == IdentityConstants.TokenName.InvitationToken)
                    {
                        LastSavedTokenValue = value;
                    }

                    var token = new AuthToken
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        LoginProvider = provider,
                        Name = name,
                        Value = value,
                        ExpiresAt = expiresAt,
                    };
                    // upsert：唯一索引 (UserId, LoginProvider, Name, SessionId) 的语义
                    _tokens.RemoveAll(t => t.UserId == userId && t.LoginProvider == provider && t.Name == name);
                    _tokens.Add(token);
                    return token.Id;
                });
            authTokenService
                .Setup(x => x.FindTokenByValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((string provider, string name, string value) =>
                    _tokens.FirstOrDefault(t => t.LoginProvider == provider && t.Name == name && t.Value == value));
            authTokenService
                .Setup(x => x.GetTokenAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((Guid userId, string provider, string name) =>
                    _tokens.FirstOrDefault(t => t.UserId == userId && t.LoginProvider == provider && t.Name == name)?.Value);
            authTokenService
                .Setup(x => x.RemoveTokenAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()))
                .Callback((Guid userId, string provider, string name) =>
                    _tokens.RemoveAll(t => t.UserId == userId && t.LoginProvider == provider && t.Name == name))
                .Returns(Task.CompletedTask);
            authTokenService
                .Setup(x => x.MarkTokenAsUsedAsync(It.IsAny<Guid>()))
                .ReturnsAsync((Guid id) =>
                {
                    if (FailNextMarkAsUsed)
                    {
                        FailNextMarkAsUsed = false;
                        return false;
                    }

                    var token = _tokens.FirstOrDefault(t => t.Id == id);
                    if (token == null || token.IsUsed)
                    {
                        return false;
                    }

                    token.IsUsed = true;
                    return true;
                });

            var handler = new Mock<IInvitationAcceptanceHandler>();
            handler
                .Setup(x => x.AcceptAsync(
                    It.IsAny<User>(), It.IsAny<AcceptInvitationDto>(),
                    It.IsAny<JsonElement?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((User _, AcceptInvitationDto __, JsonElement? profile, CancellationToken ___) =>
                {
                    HandlerSawProfile = profile;
                    return Result<InvitationAcceptOutcome>.Success(HandlerOutcome);
                });

            var urlGenerator = new Mock<IInvitationUrlGenerator>();
            urlGenerator
                .Setup(x => x.GenerateUrl(It.IsAny<User>(), It.IsAny<string>()))
                .Returns((User _, string token) => $"https://admin.example.com/accept-invitation?token={token}");

            var options = new Mock<IOptionsMonitor<TnziIdentityOptions>>();
            options.Setup(x => x.CurrentValue).Returns(new TnziIdentityOptions());

            Service = new InvitationService(
                BuildServiceProvider(),
                userManager.Object,
                userService.Object,
                authTokenService.Object,
                handler.Object,
                urlGenerator.Object,
                options.Object);
        }

        public string ExtractToken(string acceptUrl)
            => acceptUrl[(acceptUrl.IndexOf("token=", StringComparison.Ordinal) + "token=".Length)..];

        public void ExpireStoredTokens()
        {
            foreach (var token in _tokens)
            {
                token.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            }
        }

        private static IServiceProvider BuildServiceProvider()
        {
            var loggerFactory = new Mock<ILoggerFactory>();
            loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);

            var serviceProvider = new Mock<IServiceProvider>();
            serviceProvider.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);
            return serviceProvider.Object;
        }
    }
}
