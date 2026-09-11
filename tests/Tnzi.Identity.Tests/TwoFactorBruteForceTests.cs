using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

/// <summary>
/// 两步验证的失败计数。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ 守的是这样一条：<b>TOTP 分支此前完全不计失败次数。</b>
/// SMS / Email 有一道 5 次 / 15 分钟的闸门，而 TOTP 在类型判断那里就 <c>return</c> 了，
/// 走不到下面的计数逻辑 —— 偏偏 TOTP 是唯一一种没有发码动作、因而也没有任何别的节流的方式。
/// 一枚活 10 分钟的 2FA 临时令牌配上无限次尝试，六位数字够得着
/// （运行时按 ±2 个时间步验证，任一时刻有 5 枚码同时有效），而框架的限流默认是关的。
/// </para>
/// <para>
/// 变异验证：把 <c>VerifyCodeAsync</c> 的 TOTP 分支改回「直接 return，不碰计数器」，
/// 本文件里的三条会红。
/// </para>
/// </remarks>
public class TwoFactorBruteForceTests
{
    private const int MaxAttempts = 5;

    private readonly Mock<IRepository<TwoFactorCode, Guid>> _repositoryMock = new();
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<ICache> _cacheMock = new();
    private readonly TwoFactorService _service;

    private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);

    public TwoFactorBruteForceTests()
    {
        var store = new Mock<IUserStore<User>>();
        _userManagerMock = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var options = new Mock<IOptionsSnapshot<IdentityOptions>>();
        options.Setup(x => x.Value).Returns(new IdentityOptions
        {
            Otp = new OtpOptions { EnableSms = true, EnableEmail = true, EnableTotp = true }
        });

        var serviceProvider = new Mock<IServiceProvider>();
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        serviceProvider.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        // 一个够用的内存计数器：本组要断言的是「计数器有没有被用上」，不是缓存实现本身。
        _cacheMock.Setup(x => x.GetCounterAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string k, CancellationToken _) => _counters.TryGetValue(k, out var v) ? v : 0);
        _cacheMock.Setup(x => x.IncrementAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string k, long by, TimeSpan _, CancellationToken __) =>
                _counters[k] = (_counters.TryGetValue(k, out var v) ? v : 0) + by);
        _cacheMock.Setup(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string k, CancellationToken _) =>
            {
                _counters.Remove(k);
                return Task.CompletedTask;
            });

        _service = new TwoFactorService(
            _repositoryMock.Object,
            _userManagerMock.Object,
            serviceProvider.Object,
            eventBus: null,
            identityOptions: options.Object,
            cache: _cacheMock.Object);
    }

    private User ArrangeUser()
    {
        var user = new User { Id = Guid.NewGuid(), UserName = "u" };
        _userManagerMock.Setup(x => x.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        return user;
    }

    private void ArrangeTotp(User user, bool valid)
        => _userManagerMock
            .Setup(x => x.VerifyTwoFactorTokenAsync(user, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(valid);

    /// <summary>★★★ 第 6 次 TOTP 尝试必须被闸门挡下（429），而不是继续放它去猜。</summary>
    [Fact]
    public async Task Totp_AfterMaxFailures_IsLockedOut()
    {
        var user = ArrangeUser();
        ArrangeTotp(user, valid: false);

        for (var i = 0; i < MaxAttempts; i++)
        {
            var attempt = await _service.VerifyCodeAsync(user.Id, "000000", TwoFactorType.Totp, VerificationCodePurpose.TwoFactor);
            attempt.Succeeded.ShouldBeFalse();
            attempt.Code.ShouldBe(400);   // 前 5 次是「码不对」
        }

        var blocked = await _service.VerifyCodeAsync(user.Id, "000000", TwoFactorType.Totp, VerificationCodePurpose.TwoFactor);
        blocked.Succeeded.ShouldBeFalse();
        blocked.Code.ShouldBe(429);       // 第 6 次是「试太多了」

        // 被闸门挡下之后不应再去验码 —— 否则闸门只是换了个返回值，猜测照样在发生。
        _userManagerMock.Verify(
            x => x.VerifyTwoFactorTokenAsync(user, It.IsAny<string>(), It.IsAny<string>()),
            Times.Exactly(MaxAttempts));
    }

    /// <summary>对照组：验证成功要清掉计数，否则用户输错几次之后即使输对也会被锁在外面。</summary>
    [Fact]
    public async Task Totp_SuccessfulVerification_ClearsTheCounter()
    {
        var user = ArrangeUser();

        ArrangeTotp(user, valid: false);
        for (var i = 0; i < MaxAttempts - 1; i++)
        {
            await _service.VerifyCodeAsync(user.Id, "000000", TwoFactorType.Totp, VerificationCodePurpose.TwoFactor);
        }

        ArrangeTotp(user, valid: true);
        (await _service.VerifyCodeAsync(user.Id, "123456", TwoFactorType.Totp, VerificationCodePurpose.TwoFactor))
            .Succeeded.ShouldBeTrue();

        ArrangeTotp(user, valid: false);
        var afterReset = await _service.VerifyCodeAsync(user.Id, "000000", TwoFactorType.Totp, VerificationCodePurpose.TwoFactor);
        afterReset.Code.ShouldBe(400);   // 计数已清零，这一次仍是「码不对」而不是 429
    }

    /// <summary>
    /// 计数按 (用户, 方式, 用途) 分桶：另一个用户不该被别人的失败连累。
    /// 这条同时钉住了键的形状 —— TOTP 没有收件地址，只能按用户 id 计数。
    /// </summary>
    [Fact]
    public async Task Totp_CounterIsScopedPerUser()
    {
        var victim = ArrangeUser();
        var other = ArrangeUser();
        ArrangeTotp(victim, valid: false);
        ArrangeTotp(other, valid: false);

        for (var i = 0; i < MaxAttempts; i++)
        {
            await _service.VerifyCodeAsync(victim.Id, "000000", TwoFactorType.Totp, VerificationCodePurpose.TwoFactor);
        }

        (await _service.VerifyCodeAsync(victim.Id, "000000", TwoFactorType.Totp, VerificationCodePurpose.TwoFactor))
            .Code.ShouldBe(429);
        (await _service.VerifyCodeAsync(other.Id, "000000", TwoFactorType.Totp, VerificationCodePurpose.TwoFactor))
            .Code.ShouldBe(400);
    }
}
