using System.Text.Json;
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
        fixture.AuthService.Verify(
            x => x.IssueTokenAsync(fixture.User, LoginMethod.Passkey),
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
            .Setup(x => x.IssueTokenAsync(It.IsAny<User>(), It.IsAny<LoginMethod>()))
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
            x => x.IssueTokenAsync(It.IsAny<User>(), It.IsAny<LoginMethod>()), Times.Never);
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

            AuthService.Setup(x => x.IssueTokenAsync(It.IsAny<User>(), It.IsAny<LoginMethod>()))
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

            Service = new PasskeyService(
                BuildServiceProvider(),
                Handler.Object,
                UserManager.Object,
                AuthService.Object,
                new Mock<IPasskeyEnrollmentTokenService>().Object,
                httpContextAccessor.Object,
                _cache,
                identityOptions.Object);
        }

        public void GivenCurrentUserIsTheFixtureUser() => _currentUser.Setup(x => x.Id).Returns(User.Id);

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
            return serviceProvider.Object;
        }
    }
}
