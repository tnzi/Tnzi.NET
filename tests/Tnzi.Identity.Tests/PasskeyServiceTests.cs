using Microsoft.Extensions.Caching.Memory;
using Tnzi.Json;
using TnziIdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

/// <summary>
/// Passkey 接线。重点不在"WebAuthn 算得对不对"（那是运行时的事），
/// 而在<strong>校验通过之后接回了框架的哪条路</strong>。
/// </summary>
public class PasskeyServiceTests
{
    // ---------------------------------------------------------------- 签发出口

    [Fact]
    public async Task CompleteAssertion_ShouldIssueThroughTheSharedExit_NotMintItsOwnToken()
    {
        var fixture = new Fixture();
        var stateId = await fixture.SeedAssertionStateAsync();
        fixture.GivenAssertionSucceeds();

        var result = await fixture.Service.CompleteAssertionAsync(
            new PasskeyCompleteDto { StateId = stateId, CredentialJson = "{}" });

        Assert.True(result.Succeeded);

        // ★★ 这条是整个 passkey 接线的核心约束。走 SignInManager.PasskeySignInAsync
        // 或自己拼一个 token，都会绕过登录守卫（IP 白名单）与会话协调器（多设备策略），
        // 而那两样正是框架花了两轮修出来的东西。
        // satisfiedFactor = Passkey：这次登录已经证明过这枚 passkey，账号若把 passkey 也设成第二因子，
        // 再问一次问的是同一件事；别的因子（TOTP / 短信）照常挑战。与邮箱验证码登录扣掉 Email 同一条规则。
        fixture.AuthService.Verify(
            x => x.IssueTokenAsync(fixture.User, LoginMethod.Passkey, TwoFactorType.Passkey),
            Times.Once);
    }

    [Fact]
    public async Task CompleteAssertion_ShouldPropagateRejectionFromTheSharedExit()
    {
        var fixture = new Fixture();
        var stateId = await fixture.SeedAssertionStateAsync();
        fixture.GivenAssertionSucceeds();

        // 登录守卫拒绝（IP 不在白名单）时，passkey 这条路径必须跟着被拒。
        fixture.AuthService
            .Setup(x => x.IssueTokenAsync(It.IsAny<User>(), It.IsAny<LoginMethod>(), It.IsAny<TwoFactorType?>()))
            .ReturnsAsync(Result<TokenResult>.Failure("Invalid username or password", 400, "VALIDATION_ERROR"));

        var result = await fixture.Service.CompleteAssertionAsync(
            new PasskeyCompleteDto { StateId = stateId, CredentialJson = "{}" });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task CompleteAssertion_ShouldWriteTheCredentialBack_ForCloneDetection()
    {
        var fixture = new Fixture();
        var stateId = await fixture.SeedAssertionStateAsync();
        fixture.GivenAssertionSucceeds();

        await fixture.Service.CompleteAssertionAsync(
            new PasskeyCompleteDto { StateId = stateId, CredentialJson = "{}" });

        // 签名计数器等字段要写回去，否则克隆凭据检测无从谈起。
        fixture.UserManager.Verify(
            x => x.AddOrUpdatePasskeyAsync(fixture.User, It.IsAny<UserPasskeyInfo>()),
            Times.Once);
    }

    // ---------------------------------------------------------------- 挑战状态

    [Fact]
    public async Task Challenge_ShouldBeSingleUse()
    {
        var fixture = new Fixture();
        var stateId = await fixture.SeedAssertionStateAsync();
        fixture.GivenAssertionSucceeds();

        var first = await fixture.Service.CompleteAssertionAsync(
            new PasskeyCompleteDto { StateId = stateId, CredentialJson = "{}" });
        var replay = await fixture.Service.CompleteAssertionAsync(
            new PasskeyCompleteDto { StateId = stateId, CredentialJson = "{}" });

        // 挑战重放是 WebAuthn 的核心威胁，用过即失效。
        Assert.True(first.Succeeded);
        Assert.False(replay.Succeeded);
    }

    [Fact]
    public async Task CompleteAssertion_ShouldRejectUnknownState()
    {
        var fixture = new Fixture();
        fixture.GivenAssertionSucceeds();

        var result = await fixture.Service.CompleteAssertionAsync(
            new PasskeyCompleteDto { StateId = "never-issued", CredentialJson = "{}" });

        Assert.False(result.Succeeded);
        // 没有挑战就不该去问运行时要不要放行。
        fixture.AuthService.Verify(
            x => x.IssueTokenAsync(It.IsAny<User>(), It.IsAny<LoginMethod>(), It.IsAny<TwoFactorType?>()), Times.Never);
    }

    [Fact]
    public async Task CompleteRegistration_ShouldRejectStateBelongingToAnotherUser()
    {
        var fixture = new Fixture();
        // 挑战是给别人签发的，但这次注册指向当前登录用户。
        var stateId = await fixture.SeedAttestationStateAsync(ownerUserId: Guid.NewGuid());
        fixture.GivenCurrentUserIsTheFixtureUser();

        var result = await fixture.Service.CompleteRegistrationAsync(
            new PasskeyCompleteDto { StateId = stateId, CredentialJson = "{}" });

        // 少了这条回校，A 可以拿自己的注册挑战配上 B 的注册令牌，把凭据挂到 B 名下。
        Assert.False(result.Succeeded);
        fixture.UserManager.Verify(
            x => x.AddOrUpdatePasskeyAsync(It.IsAny<User>(), It.IsAny<UserPasskeyInfo>()), Times.Never);
    }

    // ---------------------------------------------------------------- 准入

    [Fact]
    public async Task BeginRegistration_ShouldRequireAnIdentity()
    {
        var fixture = new Fixture();
        // 既没登录，也没有注册令牌。

        var result = await fixture.Service.BeginRegistrationAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(401, result.Code);
    }

    // ---------------------------------------------------------------- 二次确认

    [Fact]
    public async Task BeginRegistration_ForASignedInUser_RequiresStepUp()
    {
        // 已登录、没带注册令牌 = 给自己的账号新增一种登录方式。一枚被盗访问令牌借它换来的是
        // 永久的密码因子绕过（改密、撤销全部会话之后 passkey 照样能登录），与 link-token 同一判据。
        var fixture = new Fixture();
        fixture.GivenCurrentUserIsTheFixtureUser();
        fixture.GivenCreationOptionsAreProduced();
        fixture.GivenStepUpIsNotSatisfied();

        var result = await fixture.Service.BeginRegistrationAsync();

        Assert.False(result.Succeeded);
        // 与 StepUpFilter 逐字同形：默认 401 + 专用错误码 + { scope }，前端的 withStepUp 才认得它。
        Assert.Equal(401, result.Code);
        Assert.Equal(ErrorCodes.IDENTITY_STEP_UP_REQUIRED, result.ErrorCode);
        Assert.Equal(StepUpScopes.LoginMethodManage, ScopeOf(result.ErrorDetails));
        fixture.StepUp.Verify(x => x.IsSatisfiedAsync(StepUpScopes.LoginMethodManage, It.IsAny<CancellationToken>()), Times.Once);
        // 没过确认就不该签发挑战：签了等于把 complete 那一半交出去了。
        fixture.Handler.Verify(x => x.MakeCreationOptionsAsync(It.IsAny<PasskeyUserEntity>(), It.IsAny<HttpContext>()), Times.Never);
    }

    [Fact]
    public async Task BeginRegistration_ForASignedInUser_ProceedsOnceStepUpIsSatisfied()
    {
        // 防锈：确认过了就照常签发，否则上一条只证明「一律拒绝」也能通过。
        var fixture = new Fixture();
        fixture.GivenCurrentUserIsTheFixtureUser();
        fixture.GivenCreationOptionsAreProduced();

        var result = await fixture.Service.BeginRegistrationAsync();

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data!.StateId);
    }

    [Fact]
    public async Task BeginRegistration_WithAnEnrollmentToken_DoesNotAskForStepUp()
    {
        // 持令牌的人（邀请、账号恢复）本来就没有会话可供二次确认；令牌本身就是凭据。
        var fixture = new Fixture();
        fixture.GivenCreationOptionsAreProduced();
        fixture.GivenStepUpIsNotSatisfied();

        var result = await fixture.Service.BeginRegistrationAsync(Fixture.KnownEnrollmentToken);

        Assert.True(result.Succeeded);
        fixture.StepUp.Verify(x => x.IsSatisfiedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static string? ScopeOf(object? errorDetails)
        => errorDetails?.GetType().GetProperty("scope")?.GetValue(errorDetails) as string;

    [Fact]
    public async Task BeginAssertion_ShouldNotRevealWhetherTheUserExists()
    {
        var fixture = new Fixture();
        fixture.GivenRequestOptionsAreProduced();

        var known = await fixture.Service.BeginAssertionAsync(new PasskeyAssertionBeginDto { UserName = Fixture.KnownUserName });
        var unknown = await fixture.Service.BeginAssertionAsync(new PasskeyAssertionBeginDto { UserName = "nobody-here" });

        // ★ 这个端点是匿名的。区分"用户不存在"与"用户存在但没有 passkey"
        // 就等于交出一个用户名枚举预言机。
        Assert.True(known.Succeeded);
        Assert.True(unknown.Succeeded);
    }

    /// <summary>
    /// 两步验证的第二步与二次确认都在服务端已经知道是谁的情况下发起：选项必须带上这个人的凭据。
    /// </summary>
    /// <remarks>
    /// ★ 对硬件安全密钥这是必需的：YubiKey 按默认的 <c>residentKey: discouraged</c> 登记出来的凭据
    /// 不可发现，空的 <c>allowCredentials</c>（可发现凭据流程）根本找不到它 —— 症状是「系统弹窗说没有可用的密钥」。
    /// </remarks>
    [Fact]
    public async Task BeginAssertionForUser_ShouldBuildTheOptionsAroundThatUsersCredentials()
    {
        var fixture = new Fixture();
        fixture.GivenRequestOptionsAreProduced();

        var result = await fixture.Service.BeginAssertionForUserAsync(fixture.User.Id);

        Assert.True(result.Succeeded);
        fixture.Handler.Verify(x => x.MakeRequestOptionsAsync(fixture.User, It.IsAny<HttpContext>()), Times.Once);
    }

    /// <summary>已登录、没报用户名 = 「证明还是我」（二次确认）：同样要带上本人的凭据，理由同上。</summary>
    [Fact]
    public async Task BeginAssertion_WhenSignedInWithoutAUserName_UsesTheCurrentUser()
    {
        var fixture = new Fixture();
        fixture.GivenRequestOptionsAreProduced();
        fixture.GivenCurrentUserIsTheFixtureUser();

        var result = await fixture.Service.BeginAssertionAsync(new PasskeyAssertionBeginDto());

        Assert.True(result.Succeeded);
        fixture.Handler.Verify(x => x.MakeRequestOptionsAsync(fixture.User, It.IsAny<HttpContext>()), Times.Once);
    }

    /// <summary>
    /// 最后一枚凭据没了，「拿 passkey 当第二因子」的开关必须跟着关：否则登录挑战会把它过滤掉，
    /// 而它若是唯一方式，账号就等于没开 2FA 而状态页还写着开着。
    /// </summary>
    [Fact]
    public async Task DeleteCredential_WhenTheLastOneGoes_TurnsPasskeyTwoFactorOff()
    {
        var fixture = new Fixture();
        fixture.GivenCurrentUserIsTheFixtureUser();
        fixture.User.PasskeyTwoFactorEnabled = true;
        var credentialId = fixture.GivenOneRegisteredCredential();
        fixture.UserManager.Setup(x => x.RemovePasskeyAsync(fixture.User, It.IsAny<byte[]>()))
            .Callback(() => fixture.GivenNoRegisteredCredential())
            .ReturnsAsync(IdentityResult.Success);

        var result = await fixture.Service.DeleteCredentialAsync(credentialId);

        Assert.True(result.Succeeded);
        fixture.TwoFactor.Verify(x => x.DisableTwoFactorMethodAsync(fixture.User.Id, TwoFactorType.Passkey), Times.Once);
    }

    /// <summary>
    /// 删掉最后一枚凭据会拆掉「passkey 当第二因子」，与 <c>two-factor/method/disable</c> 同一后果，
    /// 必须过同一道 <see cref="StepUpScopes.TwoFactorManage"/> 二次确认：否则被盗访问令牌一次 DELETE 就绕开了它。
    /// </summary>
    [Fact]
    public async Task DeleteCredential_WhenItWouldTearDownPasskeyTwoFactor_RequiresStepUp()
    {
        var fixture = new Fixture();
        fixture.GivenCurrentUserIsTheFixtureUser();
        fixture.User.PasskeyTwoFactorEnabled = true;
        var credentialId = fixture.GivenOneRegisteredCredential();
        fixture.GivenStepUpIsNotSatisfied();

        var result = await fixture.Service.DeleteCredentialAsync(credentialId);

        Assert.False(result.Succeeded);
        Assert.Equal(401, result.Code);
        Assert.Equal(ErrorCodes.IDENTITY_STEP_UP_REQUIRED, result.ErrorCode);
        Assert.Equal(StepUpScopes.TwoFactorManage, ScopeOf(result.ErrorDetails));
        fixture.UserManager.Verify(x => x.RemovePasskeyAsync(It.IsAny<User>(), It.IsAny<byte[]>()), Times.Never);
        fixture.TwoFactor.Verify(x => x.DisableTwoFactorMethodAsync(It.IsAny<Guid>(), It.IsAny<TwoFactorType>()), Times.Never);
    }

    /// <summary>删的不是最后一枚、或 passkey 本就不是第二因子：什么都不拆，不该要确认。</summary>
    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, -1)]
    public async Task DeleteCredential_ThatDoesNotTouchTwoFactor_DoesNotAskForStepUp(bool passkeyTwoFactor, int registered)
    {
        var fixture = new Fixture();
        fixture.GivenCurrentUserIsTheFixtureUser();
        fixture.User.PasskeyTwoFactorEnabled = passkeyTwoFactor;
        var credentialId = fixture.GivenOneRegisteredCredential(remainingAfterRemoval: registered);
        fixture.UserManager.Setup(x => x.RemovePasskeyAsync(fixture.User, It.IsAny<byte[]>()))
            .ReturnsAsync(IdentityResult.Success);
        fixture.GivenStepUpIsNotSatisfied();

        var result = await fixture.Service.DeleteCredentialAsync(credentialId);

        Assert.True(result.Succeeded);
        fixture.StepUp.Verify(x => x.IsSatisfiedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>还有别的凭据时开关留着：删一把备用钥匙不该关掉整扇门。</summary>
    [Fact]
    public async Task DeleteCredential_WhenOthersRemain_LeavesPasskeyTwoFactorOn()
    {
        var fixture = new Fixture();
        fixture.GivenCurrentUserIsTheFixtureUser();
        fixture.User.PasskeyTwoFactorEnabled = true;
        var credentialId = fixture.GivenOneRegisteredCredential(remainingAfterRemoval: 1);
        fixture.UserManager.Setup(x => x.RemovePasskeyAsync(fixture.User, It.IsAny<byte[]>()))
            .ReturnsAsync(IdentityResult.Success);

        var result = await fixture.Service.DeleteCredentialAsync(credentialId);

        Assert.True(result.Succeeded);
        fixture.TwoFactor.Verify(x => x.DisableTwoFactorMethodAsync(It.IsAny<Guid>(), It.IsAny<TwoFactorType>()), Times.Never);
    }

    /// <summary>
    /// 挑战状态必须经得起 JSON 往返 —— 多实例部署下它落在分布式缓存里。
    /// </summary>
    /// <remarks>
    /// ★ 其余用例都跑在 <c>MemoryCacheService</c> 上，那条路径存的是对象引用，
    /// <strong>永远不会暴露序列化问题</strong>；而文档写着"多实例部署需要分布式缓存"，
    /// 那条路径要 <c>JsonSerializer</c> 往返一次。状态若还原不回来，症状是
    /// "单实例好好的，一上负载均衡就全是挑战无效"，最难查的那一类。
    /// </remarks>
    [Fact]
    public async Task Challenge_ShouldSurviveJsonRoundTrip_ForMultiInstanceDeployments()
    {
        var fixture = new Fixture(serializingCache: true);
        var stateId = await fixture.SeedAssertionStateAsync();
        fixture.GivenAssertionSucceeds();

        var result = await fixture.Service.CompleteAssertionAsync(
            new PasskeyCompleteDto { StateId = stateId, CredentialJson = "{}" });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AllEntryPoints_ShouldRefuseWhenDisabled()
    {
        var fixture = new Fixture(passkeyEnabled: false);

        var begin = await fixture.Service.BeginRegistrationAsync();
        var complete = await fixture.Service.CompleteRegistrationAsync(new PasskeyCompleteDto { StateId = "x", CredentialJson = "{}" });
        var assertBegin = await fixture.Service.BeginAssertionAsync(new PasskeyAssertionBeginDto());
        var assertComplete = await fixture.Service.CompleteAssertionAsync(new PasskeyCompleteDto { StateId = "x", CredentialJson = "{}" });
        var list = await fixture.Service.GetCredentialsAsync();
        var remove = await fixture.Service.DeleteCredentialAsync("x");

        foreach (var code in new[] { begin.ErrorCode, complete.ErrorCode, assertBegin.ErrorCode, assertComplete.ErrorCode, list.ErrorCode, remove.ErrorCode })
        {
            Assert.Equal("CONFIGURATION_ERROR", code);
        }
    }

    private sealed class Fixture
    {
        public const string KnownUserName = "passkey-user";

        public PasskeyService Service { get; }

        public Mock<IAuthService> AuthService { get; } = new();

        public Mock<UserManager<User>> UserManager { get; }

        public Mock<IPasskeyHandler<User>> Handler { get; } = new();

        /// <summary>默认已确认：只有专门验二次确认的用例才把它翻成未确认。</summary>
        public Mock<IStepUpService> StepUp { get; } = new();

        /// <summary>删掉最后一枚凭据时被叫去关「passkey 当第二因子」的那一个。</summary>
        public Mock<ITwoFactorService> TwoFactor { get; } = new();

        public User User { get; } = new() { Id = Guid.NewGuid(), UserName = KnownUserName };

        private readonly ICache _cache;
        private readonly Mock<ICurrentUser> _currentUser = new();

        public Fixture(bool passkeyEnabled = true, bool serializingCache = false)
        {
            var store = new Mock<IUserStore<User>>();
            UserManager = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
            UserManager.Setup(x => x.FindByIdAsync(User.Id.ToString())).ReturnsAsync(User);
            UserManager.Setup(x => x.FindByNameAsync(KnownUserName)).ReturnsAsync(User);
            UserManager.Setup(x => x.FindByNameAsync(It.Is<string>(n => n != KnownUserName))).ReturnsAsync((User?)null);
            UserManager.Setup(x => x.AddOrUpdatePasskeyAsync(It.IsAny<User>(), It.IsAny<UserPasskeyInfo>()))
                .ReturnsAsync(IdentityResult.Success);

            AuthService.Setup(x => x.IssueTokenAsync(It.IsAny<User>(), It.IsAny<LoginMethod>(), It.IsAny<TwoFactorType?>()))
                .ReturnsAsync(Result<TokenResult>.Success(new TokenResult { AccessToken = "issued-through-shared-exit" }));

            var memoryCache = new MemoryCacheService(
                new MemoryCache(new MemoryCacheOptions()),
                new Mock<ILogger<MemoryCacheService>>().Object,
                // 全限定：GlobalUsings 里的 Tnzi.Identity.Options 会让裸 `Options` 产生歧义。
                Microsoft.Extensions.Options.Options.Create(new CachingOptions()));

            _cache = serializingCache ? new JsonRoundTripCache(memoryCache) : memoryCache;

            var identityOptions = new Mock<IOptionsMonitor<TnziIdentityOptions>>();
            var options = new TnziIdentityOptions();
            options.Passkey.Enabled = passkeyEnabled;
            identityOptions.Setup(x => x.CurrentValue).Returns(options);

            var httpContextAccessor = new Mock<IHttpContextAccessor>();
            httpContextAccessor.Setup(x => x.HttpContext).Returns(new DefaultHttpContext());

            StepUp.Setup(x => x.IsSatisfiedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

            EnrollmentTokens.Setup(x => x.ValidateAsync(KnownEnrollmentToken))
                .ReturnsAsync(Result<Guid>.Success(User.Id));

            Service = new PasskeyService(
                BuildServiceProvider(),
                Handler.Object,
                UserManager.Object,
                AuthService.Object,
                EnrollmentTokens.Object,
                httpContextAccessor.Object,
                _cache,
                identityOptions.Object);
        }

        public const string KnownEnrollmentToken = "enrollment-token";

        public Mock<IPasskeyEnrollmentTokenService> EnrollmentTokens { get; } = new();

        public void GivenStepUpIsNotSatisfied()
            => StepUp.Setup(x => x.IsSatisfiedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        public void GivenCreationOptionsAreProduced()
            => Handler.Setup(x => x.MakeCreationOptionsAsync(It.IsAny<PasskeyUserEntity>(), It.IsAny<HttpContext>()))
                .ReturnsAsync(new PasskeyCreationOptionsResult
                {
                    CreationOptionsJson = "{\"challenge\":\"x\"}",
                    AttestationState = "attestation-state"
                });

        public void GivenCurrentUserIsTheFixtureUser() => _currentUser.Setup(x => x.Id).Returns(User.Id);

        /// <summary>
        /// 账号名下有一枚凭据；返回它的 base64url 标识。<paramref name="remainingAfterRemoval"/>
        /// 是删除之后 <c>GetPasskeysAsync</c> 还报出多少枚（默认还是这一枚，由删除用例的 Callback 改成 0）。
        /// </summary>
        public string GivenOneRegisteredCredential(int remainingAfterRemoval = -1)
        {
            var info = CreatePasskeyInfo();
            UserManager.Setup(x => x.GetPasskeyAsync(User, It.IsAny<byte[]>())).ReturnsAsync(info);
            var remaining = remainingAfterRemoval < 0
                ? new List<UserPasskeyInfo> { info }
                : Enumerable.Range(0, remainingAfterRemoval).Select(_ => CreatePasskeyInfo()).ToList();
            UserManager.Setup(x => x.GetPasskeysAsync(User)).ReturnsAsync(remaining);
            return Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(info.CredentialId);
        }

        public void GivenNoRegisteredCredential()
            => UserManager.Setup(x => x.GetPasskeysAsync(User)).ReturnsAsync(new List<UserPasskeyInfo>());

        public void GivenAssertionSucceeds()
            => Handler.Setup(x => x.PerformAssertionAsync(It.IsAny<PasskeyAssertionContext>()))
                .ReturnsAsync(PasskeyAssertionResult.Success(CreatePasskeyInfo(), User));

        public void GivenRequestOptionsAreProduced()
            => Handler.Setup(x => x.MakeRequestOptionsAsync(It.IsAny<User?>(), It.IsAny<HttpContext>()))
                .ReturnsAsync(new PasskeyRequestOptionsResult
                {
                    RequestOptionsJson = "{\"challenge\":\"x\"}",
                    AssertionState = "assertion-state"
                });

        /// <summary>直接往缓存里放一份挑战状态，绕开 begin（begin 自己另有用例覆盖）。</summary>
        public async Task<string> SeedAssertionStateAsync()
        {
            GivenRequestOptionsAreProduced();
            var begun = await Service.BeginAssertionAsync(new PasskeyAssertionBeginDto());
            return begun.Data!.StateId;
        }

        public async Task<string> SeedAttestationStateAsync(Guid ownerUserId)
        {
            var owner = new User { Id = ownerUserId, UserName = "someone-else" };
            UserManager.Setup(x => x.FindByIdAsync(ownerUserId.ToString())).ReturnsAsync(owner);
            _currentUser.Setup(x => x.Id).Returns(ownerUserId);

            Handler.Setup(x => x.MakeCreationOptionsAsync(It.IsAny<PasskeyUserEntity>(), It.IsAny<HttpContext>()))
                .ReturnsAsync(new PasskeyCreationOptionsResult
                {
                    CreationOptionsJson = "{\"challenge\":\"x\"}",
                    AttestationState = "attestation-state"
                });

            var begun = await Service.BeginRegistrationAsync();
            return begun.Data!.StateId;
        }

        private static UserPasskeyInfo CreatePasskeyInfo() => new(
            credentialId: [1, 2, 3],
            publicKey: [4, 5, 6],
            createdAt: DateTimeOffset.UtcNow,
            signCount: 1,
            transports: ["internal"],
            isUserVerified: true,
            isBackupEligible: true,
            isBackedUp: false,
            attestationObject: [7],
            clientDataJson: [8]);

        /// <summary>
        /// 用 <c>TnziJsonDefaults.Options</c> 把值序列化再还原，模拟分布式缓存那条路径。
        /// </summary>
        /// <remarks>
        /// 只实现 passkey 用到的三个成员，其余显式不支持 —— 一个假装什么都能干的桩，
        /// 会在被用到新方法时静默返回 default，把缺陷伪装成通过。
        /// </remarks>
        private sealed class JsonRoundTripCache(ICache inner) : ICache
        {
            public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
            {
                var json = await inner.GetAsync<string>(key, cancellationToken);
                return json == null ? default : JsonSerializer.Deserialize<T>(json, TnziJsonDefaults.Options);
            }

            public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
                => inner.SetAsync(key, JsonSerializer.Serialize(value, TnziJsonDefaults.Options), expiration, cancellationToken);

            public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
                => inner.RemoveAsync(key, cancellationToken);

            public Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<long> IncrementAsync(string key, long increment = 1, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<long> IncrementAsync(string key, long increment, TimeSpan expiration, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<long> DecrementAsync(string key, long decrement = 1, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<bool> TrySetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task SetWithTagsAsync<T>(string key, T value, IEnumerable<string> tags, TimeSpan? expiration = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<Dictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task SetManyAsync<T>(IEnumerable<KeyValuePair<string, T>> items, TimeSpan? expiration = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        }

        private IServiceProvider BuildServiceProvider()
        {
            var loggerFactory = new Mock<ILoggerFactory>();
            loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);

            var serviceProvider = new Mock<IServiceProvider>();
            serviceProvider.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);
            serviceProvider.Setup(x => x.GetService(typeof(ICurrentUser))).Returns(_currentUser.Object);
            // 服务在调用点解析 IStepUpService（StepUpService 反向依赖 IPasskeyService，构造注入会成环）。
            serviceProvider.Setup(x => x.GetService(typeof(IStepUpService))).Returns(() => StepUp.Object);
            serviceProvider.Setup(x => x.GetService(typeof(ITwoFactorService))).Returns(() => TwoFactor.Object);
            TwoFactor.Setup(x => x.DisableTwoFactorMethodAsync(It.IsAny<Guid>(), It.IsAny<TwoFactorType>())).ReturnsAsync(Result.Success());
            return serviceProvider.Object;
        }
    }
}
