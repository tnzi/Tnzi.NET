using System.Security.Cryptography;
using Tnzi.Security.Authorization;
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
    /// ★★★ 发出邀请时不宣布「已注册」：那一刻账号没有密码、登录不进去，而框架自带的订阅者会据此
    /// 发一封「账号已可用」的欢迎邮件，被邀请人先收到它、再收到真正的邀请。
    /// </summary>
    [Fact]
    public async Task Invite_DoesNotAnnounceARegistration()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.InviteAsync(NewInvitation());

        Assert.True(result.Succeeded);
        fixture.UserService.Verify(x => x.CreateAsync(It.IsAny<CreateUserDto>()), Times.Never);
        fixture.EventBus.Verify(x => x.PublishAsync(It.IsAny<UserRegisteredEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.EventBus.Verify(x => x.PublishAsync(It.IsAny<UserInvitedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>★★ 注册事件在接受邀请、账号真正可用的那一刻发布，且只发一次。</summary>
    [Fact]
    public async Task Accept_AnnouncesTheRegistrationOnce_WhenTheAccountBecomesUsable()
    {
        var fixture = new Fixture();
        var token = fixture.ExtractToken((await fixture.Service.InviteAsync(NewInvitation())).Data!.AcceptUrl);

        await fixture.Service.AcceptAsync(new AcceptInvitationDto { Token = token, Password = "P@ssw0rd!" });
        await fixture.Service.AcceptAsync(new AcceptInvitationDto { Token = token, Password = "P@ssw0rd!" });

        fixture.EventBus.Verify(
            x => x.PublishAsync(
                It.Is<UserRegisteredEvent>(e => e.UserId == fixture.CreatedUser!.Id && e.Email == "newhire@example.com"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>还没办完（handler 说还差一步）或激活写失败：账号仍不可用，不宣布注册。</summary>
    [Fact]
    public async Task Accept_DoesNotAnnounceARegistration_UntilTheAccountIsActivated()
    {
        var fixture = new Fixture { HandlerOutcome = InvitationAcceptOutcome.NeedsMore("SetPassword") };
        var token = fixture.ExtractToken((await fixture.Service.InviteAsync(NewInvitation())).Data!.AcceptUrl);

        await fixture.Service.AcceptAsync(new AcceptInvitationDto { Token = token });
        fixture.HandlerOutcome = InvitationAcceptOutcome.Done();
        fixture.FailUserUpdate = true;
        await fixture.Service.AcceptAsync(new AcceptInvitationDto { Token = token, Password = "P@ssw0rd!" });

        fixture.EventBus.Verify(x => x.PublishAsync(It.IsAny<UserRegisteredEvent>(), It.IsAny<CancellationToken>()), Times.Never);
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
    /// 本地部分与域名都以 emoji 开头：首字母按码元取会露出半个代理项（预览页上是一个替换字符），所以那一段一个字符都不露。
    /// </summary>
    [Fact]
    public async Task Preview_EmailStartingWithAnEmoji_ShowsNoLoneSurrogate()
    {
        var fixture = new Fixture();
        var invitation = NewInvitation();
        invitation.Email = "\U0001F600hire@\U0001F600mail.com";
        var token = fixture.ExtractToken((await fixture.Service.InviteAsync(invitation)).Data!.AcceptUrl);

        var masked = (await fixture.Service.PreviewAsync(token)).Data!.MaskedEmail!;

        // 本地部分 6 个码元、域名主体 6 个码元，都整段遮掉；顶级域照留
        Assert.Equal("******@******.com", masked);
        Assert.DoesNotContain(masked, char.IsSurrogate);
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
    /// ★★★ 重发邀请会把一条新的接受链接交给调用者：对一个预设了超管角色、还没接受的账号，
    /// 持 <c>user.update</c> 的普通管理员拿它设上自己的密码、接受邀请，就以超管身份登录了。
    /// </summary>
    /// <remarks>
    /// 同一个权限码下的重置密码早就挂着超管护栏，重发邀请是同一件事（决定谁能以这个账号登录）换了一条路。
    /// 拒绝必须发生在动令牌之前：被邀请人手里那条链接要原样可用。
    /// </remarks>
    [Fact]
    public async Task Resend_ForAPendingSuperAdmin_ByANonSuperAdmin_IsForbidden_AndLeavesTheLinkAlone()
    {
        var fixture = Fixture.WithANonSuperAdminActorAgainstASuperAdminTarget();
        var original = fixture.ExtractToken((await fixture.Service.InviteAsync(NewInvitation())).Data!.AcceptUrl);
        var savedBefore = fixture.LastSavedTokenValue;

        var result = await fixture.Service.ResendAsync(fixture.CreatedUser!.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(403, result.Code);
        Assert.Equal(ErrorCodes.FORBIDDEN, result.ErrorCode);
        Assert.Null(result.Data);
        Assert.Equal(savedBefore, fixture.LastSavedTokenValue);
        Assert.True((await fixture.Service.PreviewAsync(original)).Succeeded);
    }

    /// <summary>
    /// 删账号那一步对超管目标答 403：先拦下来，管理员看到的回答与重发一致，被邀请人的链接原样可用。
    /// </summary>
    [Fact]
    public async Task Revoke_ForAPendingSuperAdmin_ByANonSuperAdmin_IsForbidden_AndKeepsTheLink()
    {
        var fixture = Fixture.WithANonSuperAdminActorAgainstASuperAdminTarget();
        var token = fixture.ExtractToken((await fixture.Service.InviteAsync(NewInvitation())).Data!.AcceptUrl);

        var result = await fixture.Service.RevokeAsync(fixture.CreatedUser!.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(403, result.Code);
        Assert.False(fixture.UserDeleted);
        Assert.True((await fixture.Service.PreviewAsync(token)).Succeeded);
    }

    /// <summary>
    /// ★★ 删账号可能被拒（消费方的 <c>IUserUsageProvider</c> 声明账号在用时 409）：此前令牌先作废，
    /// 管理员看到 409 以为什么都没发生，被邀请人手里的链接却已经打不开了，账号停在 Pending、再没有令牌。
    /// </summary>
    [Fact]
    public async Task Revoke_WhenTheDeleteIsRefused_KeepsTheLink()
    {
        var fixture = new Fixture
        {
            DeleteResult = Result.Failure("A staff record is linked to this account", 409, ErrorCodes.IDENTITY_USER_IN_USE),
        };
        var token = fixture.ExtractToken((await fixture.Service.InviteAsync(NewInvitation())).Data!.AcceptUrl);

        var result = await fixture.Service.RevokeAsync(fixture.CreatedUser!.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Code);
        Assert.Equal(ErrorCodes.IDENTITY_USER_IN_USE, result.ErrorCode);
        Assert.True((await fixture.Service.PreviewAsync(token)).Succeeded);
    }

    /// <summary>
    /// ★ 预填资料只在「已邀请、还没接受」的窗口里有意义。它不带过期时间、撤销又是软删，
    /// 此前接受与撤销都不删它，管理员预填的个人资料在每一份邀请上无限期留存。
    /// </summary>
    [Fact]
    public async Task Accept_WhenDone_RemovesTheAdminSuppliedProfile()
    {
        var fixture = new Fixture();
        var token = fixture.ExtractToken((await fixture.Service.InviteAsync(NewInvitationWithProfile())).Data!.AcceptUrl);
        Assert.True(fixture.HasStoredToken(ProfileTokenName));

        await fixture.Service.AcceptAsync(new AcceptInvitationDto { Token = token, Password = "P@ssw0rd!" });

        Assert.False(fixture.HasStoredToken(ProfileTokenName));
    }

    /// <summary>对照：handler 说还没做完时资料留着，用户拿同一条链接回来时还要用它当初值。</summary>
    [Fact]
    public async Task Accept_WhenIncomplete_KeepsTheProfileForTheNextAttempt()
    {
        var fixture = new Fixture { HandlerOutcome = InvitationAcceptOutcome.NeedsMore("EnrollTotp") };
        var token = fixture.ExtractToken((await fixture.Service.InviteAsync(NewInvitationWithProfile())).Data!.AcceptUrl);

        await fixture.Service.AcceptAsync(new AcceptInvitationDto { Token = token });

        Assert.True(fixture.HasStoredToken(ProfileTokenName));
    }

    [Fact]
    public async Task Revoke_RemovesTheAdminSuppliedProfile()
    {
        var fixture = new Fixture();
        await fixture.Service.InviteAsync(NewInvitationWithProfile());

        var result = await fixture.Service.RevokeAsync(fixture.CreatedUser!.Id);

        Assert.True(result.Succeeded);
        Assert.False(fixture.HasStoredToken(ProfileTokenName));
    }

    /// <summary>
    /// ★ 有效期在建号之前校验：此前 int.MaxValue 小时到签发那一步才在 AddHours 上抛异常，
    /// 账号已经建好、置成 Pending，留下一个没有令牌的待接受账号与一个 500；而 876000 小时签出的是百年有效的匿名凭据。
    /// </summary>
    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(InvitationOptions.MaxLinkLifetimeHours + 1)]
    [InlineData(0)]
    public async Task Invite_WithAnOutOfRangeLifetime_IsRejectedBeforeCreatingAnAccount(int lifetimeHours)
    {
        var fixture = new Fixture();
        var input = NewInvitation();
        input.LifetimeHours = lifetimeHours;

        var result = await fixture.Service.InviteAsync(input);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.Equal(ErrorCodes.VALIDATION_ERROR, result.ErrorCode);
        Assert.Null(fixture.CreatedUser);
    }

    [Fact]
    public async Task Invite_AtTheMaximumLifetime_IsIssued()
    {
        var fixture = new Fixture();
        var input = NewInvitation();
        input.LifetimeHours = InvitationOptions.MaxLinkLifetimeHours;

        var result = await fixture.Service.InviteAsync(input);

        Assert.True(result.Succeeded);
        Assert.True(result.Data!.ExpiresAt > DateTime.UtcNow.AddHours(InvitationOptions.MaxLinkLifetimeHours - 1));
    }

    [Fact]
    public async Task Resend_WithAnOutOfRangeLifetime_IsRejected_AndLeavesTheLinkAlone()
    {
        var fixture = new Fixture();
        var original = fixture.ExtractToken((await fixture.Service.InviteAsync(NewInvitation())).Data!.AcceptUrl);
        var savedBefore = fixture.LastSavedTokenValue;

        var result = await fixture.Service.ResendAsync(fixture.CreatedUser!.Id, InvitationOptions.MaxLinkLifetimeHours + 1);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.Equal(savedBefore, fixture.LastSavedTokenValue);
        Assert.True((await fixture.Service.PreviewAsync(original)).Succeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(InvitationOptions.MaxLinkLifetimeHours + 1)]
    public void OptionsValidation_RejectsAConfiguredLifetimeOutOfRange(int configured)
    {
        var options = new TnziIdentityOptions();
        options.Invitation.LinkLifetimeHours = configured;

        var result = new IdentityOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Invitation.LinkLifetimeHours"));
    }

    private const string ProfileTokenName = "InvitationProfile";

    private static CreateInvitationDto NewInvitationWithProfile()
    {
        using var doc = JsonDocument.Parse("""{"employeeNo":"E-1024"}""");
        var input = NewInvitation();
        input.Profile = doc.RootElement.Clone();
        return input;
    }

    /// <summary>对照：超管替超管重发照常可以：护栏拦的是越级，不是这件事本身。</summary>
    [Fact]
    public async Task Resend_ForAPendingSuperAdmin_ByASuperAdmin_IsAllowed()
    {
        var fixture = Fixture.WithANonSuperAdminActorAgainstASuperAdminTarget(actorIsSuperAdmin: true);
        var first = fixture.ExtractToken((await fixture.Service.InviteAsync(NewInvitation())).Data!.AcceptUrl);

        var result = await fixture.Service.ResendAsync(fixture.CreatedUser!.Id);

        Assert.True(result.Succeeded);
        Assert.NotEqual(first, fixture.ExtractToken(result.Data!.AcceptUrl));
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

        public Mock<IUserService> UserService { get; private set; } = null!;

        public Mock<IEventBus> EventBus { get; } = new();

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

        /// <summary><c>IUserService.DeleteAsync</c> 的回答；为空或成功时照常删除。</summary>
        public Result? DeleteResult { get; set; }

        public bool HasStoredToken(string name) => _tokens.Any(t => t.Name == name);

        /// <summary>
        /// 调用者是 <paramref name="actorIsSuperAdmin"/> 指定身份的管理员，而被邀请的账号（调用者之外的任何人）是超管。
        /// </summary>
        public static Fixture WithANonSuperAdminActorAgainstASuperAdminTarget(bool actorIsSuperAdmin = false)
        {
            var actorId = Guid.NewGuid();
            var functionAuthorization = new Mock<IFunctionAuthorizationService>();
            functionAuthorization.Setup(a => a.IsSuperAdminAsync(It.Is<Guid>(id => id != actorId))).ReturnsAsync(true);
            functionAuthorization.Setup(a => a.IsSuperAdminAsync(actorId)).ReturnsAsync(actorIsSuperAdmin);
            return new Fixture(actorId, functionAuthorization.Object);
        }

        public Fixture(Guid? actorId = null, IFunctionAuthorizationService? functionAuthorization = null)
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

            UserService = new Mock<IUserService>();
            var userService = UserService;
            userService
                .Setup(x => x.CreateForInvitationAsync(It.IsAny<CreateUserDto>()))
                .ReturnsAsync((CreateUserDto input) =>
                {
                    LastCreateInput = input;
                    CreatedUser = new User
                    {
                        Id = Guid.NewGuid(),
                        UserName = input.UserName ?? input.Email,
                        Email = input.Email,
                        PhoneNumber = input.PhoneNumber,
                    };
                    return Result<UserDto>.Success(new UserDto { Id = CreatedUser.Id, UserName = CreatedUser.UserName ?? string.Empty });
                });
            userService
                .Setup(x => x.DeleteAsync(It.IsAny<Guid>()))
                .ReturnsAsync(() =>
                {
                    if (DeleteResult is { Succeeded: false } refused)
                    {
                        return refused;
                    }

                    UserDeleted = true;
                    return Result.Success();
                });

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
                BuildServiceProvider(actorId, EventBus.Object),
                userManager.Object,
                userService.Object,
                authTokenService.Object,
                handler.Object,
                urlGenerator.Object,
                options.Object,
                UnscopedUserTenantScope.Create(),
                functionAuthorization: functionAuthorization);
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

        private static IServiceProvider BuildServiceProvider(Guid? actorId, IEventBus eventBus)
        {
            var loggerFactory = new Mock<ILoggerFactory>();
            loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);

            var serviceProvider = new Mock<IServiceProvider>();
            serviceProvider.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);
            serviceProvider.Setup(x => x.GetService(typeof(IEventBus))).Returns(eventBus);
            if (actorId.HasValue)
            {
                var currentUser = new Mock<ICurrentUser>();
                currentUser.SetupGet(c => c.Id).Returns(actorId.Value);
                currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
                serviceProvider.Setup(x => x.GetService(typeof(ICurrentUser))).Returns(currentUser.Object);
            }

            return serviceProvider.Object;
        }
    }
}
