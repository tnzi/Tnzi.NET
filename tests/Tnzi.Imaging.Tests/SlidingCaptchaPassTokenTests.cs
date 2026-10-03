using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tnzi.AspNetCore.Security;
using Tnzi.ScopedContext;

namespace Tnzi.Imaging.Tests;

/// <summary>
/// 滑块验证通过之后签出的通行令牌：这是滑块从「孤立控件」变成一家 <see cref="ICaptchaProvider"/> 的那一环。
/// </summary>
/// <remarks>
/// 此前 <c>/captcha/sliding/verify</c> 只回 <c>{ Success }</c>，受保护端点无从分辨这次提交前有没有真的滑过 ——
/// 一个跳过滑块直接 POST 的机器人与一个滑过的人在服务端看来一模一样。
/// 用真实的内存缓存跑（不是 mock）：一次性与「删了再判」两条都是缓存操作顺序的问题，mock 证明不了。
/// </remarks>
public class SlidingCaptchaPassTokenTests
{
    private static (SlidingCaptchaService Service, ICache Cache) CreateService(int readBarrier = 0)
    {
        var scopedContext = new Mock<IScopedContext>();
        scopedContext.Setup(c => c.ClientIpAddress).Returns("203.0.113.7");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(scopedContext.Object);
        var provider = services.BuildServiceProvider();

        var cache = new MemoryCacheService(
            new MemoryCache(new MemoryCacheOptions()),
            Mock.Of<ILogger<MemoryCacheService>>(),
            Microsoft.Extensions.Options.Options.Create(new CachingOptions()),
            provider);
        ICache effective = readBarrier > 0 ? BarrierCache.Wrap(cache, readBarrier) : cache;

        var options = Microsoft.Extensions.Options.Options.Create(new ImagingOptions());
        return (new SlidingCaptchaService(provider, options, effective), effective);
    }

    /// <summary>生成一道题并直接读出正确答案（测试只关心令牌那一环，不关心图片）。</summary>
    private static async Task<(string Token, int CorrectX)> GenerateAsync(SlidingCaptchaService service, ICache cache, string? purpose)
    {
        var generated = await service.GenerateAsync(purpose: purpose);
        Assert.True(generated.Succeeded);
        var stored = await cache.GetAsync<SlidingCaptchaStoredData>($"SlidingCaptcha:{generated.Data!.Token}");
        Assert.NotNull(stored);
        return (generated.Data.Token, stored!.CorrectX);
    }

    [Fact]
    public async Task SuccessfulVerify_IssuesAPassToken_FailedVerifyDoesNot()
    {
        var (service, cache) = CreateService();
        var (token, correctX) = await GenerateAsync(service, cache, "login");

        var passed = await service.VerifyAsync(token, correctX);

        Assert.True(passed.Data!.Success);
        Assert.False(string.IsNullOrEmpty(passed.Data.PassToken));

        var (token2, correctX2) = await GenerateAsync(service, cache, "login");
        var failed = await service.VerifyAsync(token2, correctX2 + 100);

        Assert.False(failed.Data!.Success);
        Assert.Null(failed.Data.PassToken);
    }

    [Fact]
    public async Task PassToken_RedeemsOnce_UnderTheBoundPurpose()
    {
        var (service, cache) = CreateService();
        var (token, correctX) = await GenerateAsync(service, cache, "login");
        var pass = (await service.VerifyAsync(token, correctX)).Data!.PassToken!;

        Assert.True(await service.RedeemPassTokenAsync(pass, "login"));
        Assert.False(await service.RedeemPassTokenAsync(pass, "login"));
    }

    [Fact]
    public async Task PassToken_BoundToAnotherPurpose_IsRefused_AndBurned()
    {
        var (service, cache) = CreateService();
        var (token, correctX) = await GenerateAsync(service, cache, "register");
        var pass = (await service.VerifyAsync(token, correctX)).Data!.PassToken!;

        Assert.False(await service.RedeemPassTokenAsync(pass, "login"));
        // 试错的那一次已经把它烧掉：拿一枚令牌逐个用途试是不允许的。
        Assert.False(await service.RedeemPassTokenAsync(pass, "register"));
    }

    [Fact]
    public async Task PassToken_WithoutABoundPurpose_IsRefusedByEveryGate()
    {
        // 核销方总是带着用途来的。若不绑用途的令牌哪儿都收，用途绑定就成了客户端的选项：
        // 出题时省掉 ?purpose= 即得一枚万能令牌，「注册页的令牌在登录页一律拒绝」落空。
        var (service, cache) = CreateService();
        var (token, correctX) = await GenerateAsync(service, cache, purpose: null);
        var pass = (await service.VerifyAsync(token, correctX)).Data!.PassToken!;

        Assert.False(await service.RedeemPassTokenAsync(pass, "contact"));
    }

    [Fact]
    public async Task ConcurrentVerifies_OfOnePuzzle_IssueAtMostOnePassToken()
    {
        // 同一道题并发 N 个不同 X 的提交：若它们都在删除之前读到答案，穷举一次就能命中。
        // 这里全部带正确答案，读侧用屏障把 N 个请求卡在「都读到了」之后再放行 —— 一次性若只是先读后删，
        // N 个都会拿到通行令牌。
        const int attempts = 20;
        var (service, cache) = CreateService(readBarrier: attempts);
        var gate = (BarrierCache)(object)cache;
        gate.Enabled = false;
        var (token, correctX) = await GenerateAsync(service, cache, "login");
        gate.Enabled = true;

        var results = await RunConcurrentlyAsync(attempts, () => service.VerifyAsync(token, correctX));

        Assert.Equal(1, results.Count(r => r.Data!.Success));
        Assert.Equal(1, results.Count(r => r.Data!.PassToken != null));
    }

    [Fact]
    public async Task ConcurrentRedeems_OfOnePassToken_SucceedAtMostOnce()
    {
        // 同一枚通行令牌并发打 N 个 [RequireCaptcha] 请求：只能有一个过。
        const int attempts = 20;
        var (service, cache) = CreateService(readBarrier: attempts);
        var gate = (BarrierCache)(object)cache;
        gate.Enabled = false;
        var (token, correctX) = await GenerateAsync(service, cache, "login");
        var pass = (await service.VerifyAsync(token, correctX)).Data!.PassToken!;
        gate.Enabled = true;

        var results = await RunConcurrentlyAsync(attempts, () => service.RedeemPassTokenAsync(pass, "login"));

        Assert.Equal(1, results.Count(ok => ok));
    }

    private static async Task<T[]> RunConcurrentlyAsync<T>(int count, Func<Task<T>> attempt)
    {
        // 真线程：屏障是同步阻塞的，靠线程池慢慢扩容会让测试变慢甚至超时。
        var tasks = Enumerable.Range(0, count)
            .Select(_ => Task.Factory.StartNew(attempt, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap())
            .ToArray();
        return await Task.WhenAll(tasks);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("never-issued")]
    public async Task UnknownOrBlankPassToken_IsRefused(string pass)
    {
        var (service, _) = CreateService();

        Assert.False(await service.RedeemPassTokenAsync(pass, "login"));
    }

    [Fact]
    public async Task Provider_MapsRedeemToPassAndExpiredOrReplayed()
    {
        var (service, cache) = CreateService();
        var provider = new SlidingCaptchaProvider(service);
        var (token, correctX) = await GenerateAsync(service, cache, "login");
        var pass = (await service.VerifyAsync(token, correctX)).Data!.PassToken!;

        var first = await provider.VerifyAsync(new CaptchaVerificationRequest(pass, "login", null));
        var second = await provider.VerifyAsync(new CaptchaVerificationRequest(pass, "login", null));

        Assert.Equal("sliding", provider.Name);
        Assert.True(first.Passed);
        Assert.Equal("login", first.Action);
        Assert.False(second.Passed);
        Assert.Equal(CaptchaFailure.ExpiredOrReplayed, second.Failure);
        Assert.Contains("{purpose}", provider.GetClientConfig().ChallengeUrl, StringComparison.Ordinal);
    }
}

/// <summary>
/// 真实缓存的包装：只在「按拼图 / 通行令牌读数据」的那一步上设屏障，N 个并发调用者全部读到之后才一起放行。
/// 这正是先读后删的窗口 —— 没有它，内存缓存的同步完成会让并发调用在测试里天然串行化。
/// </summary>
public class BarrierCache : System.Reflection.DispatchProxy
{
    private ICache _inner = null!;
    private Barrier _barrier = null!;

    /// <summary>关掉时不设屏障（准备阶段的单线程调用用）。</summary>
    public bool Enabled { get; set; } = true;

    public static ICache Wrap(ICache inner, int participants)
    {
        var proxy = Create<ICache, BarrierCache>();
        var self = (BarrierCache)(object)proxy;
        self._inner = inner;
        self._barrier = new Barrier(participants);
        return proxy;
    }

    protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
    {
        if (Enabled
            && targetMethod!.Name == nameof(ICache.GetAsync)
            && args?.Length > 0 && args[0] is string key
            && key.StartsWith("SlidingCaptcha:", StringComparison.Ordinal))
        {
            Assert.True(_barrier.SignalAndWait(TimeSpan.FromSeconds(30)), "concurrent readers never all arrived");
        }

        try
        {
            return targetMethod!.Invoke(_inner, args);
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw ex.InnerException;
        }
    }
}
