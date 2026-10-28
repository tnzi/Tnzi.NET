using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tnzi.Imaging.Controllers;
using Tnzi.Imaging.Dtos;
using Tnzi.Results;
using Tnzi.ScopedContext;

namespace Tnzi.Imaging.Tests;

/// <summary>
/// 配置的容差与自适应难度：配置说多严，出出来的题就至少有多严。
/// </summary>
/// <remarks>
/// <para>
/// <b>被保护的缺陷一</b>：令牌里存的容差为 0 时被当成「旧令牌没记录容差」，校验回退到入参的 5 像素。
/// 校验器允许 <c>Tolerance = 0</c>（要求精确对齐），于是这条配置被静默放宽成 5。
/// </para>
/// <para>
/// <b>被保护的缺陷二</b>：自适应难度的各档容差是写死的 (5, 3, 2)，不参考配置。配置 <c>Tolerance = 2</c>
/// 时，没有失败历史的客户端反而拿到 5 像素容差，比不走自适应还宽。
/// </para>
/// <para>
/// <b>被保护的缺陷三</b>：<c>/generate</c> 出的题不记客户端键，失败不计数，而且这个端点就是
/// <c>sliding</c> 提供商下发给客户端的出题地址。框架自己的接线里自适应难度从未生效；即便客户端改用
/// <c>generate-adaptive</c>，攻击脚本只要调 <c>/generate</c> 就永远拿到最简单的题与等价的通行令牌。
/// </para>
/// </remarks>
public class SlidingCaptchaDifficultyTests
{
    private const string ClientIp = "203.0.113.7";

    [Fact]
    public async Task ConfiguredZeroTolerance_IsEnforced_NotWidenedToTheFallback()
    {
        var (service, cache) = CreateService(new SlidingCaptchaOptions { Tolerance = 0 });

        var generated = await service.GenerateAsync(purpose: "login");
        var stored = await cache.GetAsync<SlidingCaptchaStoredData>($"SlidingCaptcha:{generated.Data!.Token}");

        var offByThree = await service.VerifyAsync(generated.Data.Token, stored!.CorrectX + 3);

        offByThree.Data!.Success.ShouldBeFalse("配置的容差是 0，偏 3 像素不该通过");
    }

    [Fact]
    public async Task ConfiguredZeroTolerance_StillAcceptsTheExactPosition()
    {
        var (service, cache) = CreateService(new SlidingCaptchaOptions { Tolerance = 0 });

        var generated = await service.GenerateAsync(purpose: "login");
        var stored = await cache.GetAsync<SlidingCaptchaStoredData>($"SlidingCaptcha:{generated.Data!.Token}");

        var exact = await service.VerifyAsync(generated.Data.Token, stored!.CorrectX);

        exact.Data!.Success.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(4, 2)]
    [InlineData(9, 2)]
    public async Task AdaptiveTolerance_NeverExceedsTheConfiguredTolerance(long failures, int configured)
    {
        var cache = new CounterCache(failures);
        var service = CreateService(cache.Object, new SlidingCaptchaOptions { Tolerance = configured });

        await service.GenerateAdaptiveAsync("login");

        cache.Stored.ShouldNotBeNull();
        int? tolerance = cache.Stored!.Tolerance;
        tolerance.ShouldNotBeNull();
        tolerance!.Value.ShouldBeLessThanOrEqualTo(configured);
    }

    [Fact]
    public async Task AdaptiveTolerance_StillTightensBelowALooserConfiguration()
    {
        var cache = new CounterCache(9);
        var service = CreateService(cache.Object, new SlidingCaptchaOptions { Tolerance = 8 });

        await service.GenerateAdaptiveAsync("login");

        cache.Stored!.Tolerance.ShouldBe(2, "失败 9 次落到最高难度那一档");
    }

    [Fact]
    public async Task WithoutFailures_TheAdaptivePuzzleKeepsTheConfiguredTolerance()
    {
        var cache = new CounterCache(0);
        var service = CreateService(cache.Object, new SlidingCaptchaOptions { Tolerance = 8 });

        await service.GenerateAdaptiveAsync("login");

        cache.Stored!.Tolerance.ShouldBe(8, "没有失败历史时出的题与配置一致");
    }

    [Fact]
    public async Task PlainGenerate_RecordsTheClientKey_SoFailuresCount()
    {
        var cache = new CounterCache(0);
        var service = CreateService(cache.Object, new SlidingCaptchaOptions());

        await service.GenerateAsync(purpose: "login");

        cache.Stored!.ClientId.ShouldNotBeNullOrEmpty("没有客户端键，这道题上的失败永远不计数");
    }

    [Fact]
    public async Task FailuresOnPlainGeneratePuzzles_RaiseTheAdaptiveDifficulty()
    {
        var (service, cache) = CreateService(new SlidingCaptchaOptions());

        for (var i = 0; i < 6; i++)
        {
            var generated = await service.GenerateAsync(purpose: "login");
            var stored = await cache.GetAsync<SlidingCaptchaStoredData>($"SlidingCaptcha:{generated.Data!.Token}");
            (await service.VerifyAsync(generated.Data.Token, stored!.CorrectX + 100)).Data!.Success.ShouldBeFalse();
        }

        var adaptive = await service.GenerateAdaptiveAsync("login");
        var adaptiveStored = await cache.GetAsync<SlidingCaptchaStoredData>($"SlidingCaptcha:{adaptive.Data!.Token}");

        adaptiveStored!.Tolerance.ShouldBe(2, "6 次失败之后应当落到最高难度那一档");
    }

    [Fact]
    public async Task TheGenerateEndpoint_ServesTheAdaptivePuzzle()
    {
        // /generate 是 sliding 提供商下发给客户端的出题地址；它若不走自适应，难度就永远停在最低档。
        var service = new Mock<ISlidingCaptchaService>();
        service.Setup(s => s.GenerateAdaptiveAsync("login", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SlidingCaptchaDto>.Success(new SlidingCaptchaDto { Token = "t" }));
        var controller = new DefaultSlidingCaptchaController(service.Object);

        await controller.Generate("login");

        service.Verify(s => s.GenerateAdaptiveAsync("login", It.IsAny<CancellationToken>()), Times.Once);
        service.Verify(s => s.GenerateAsync(It.IsAny<SlidingCaptchaOptions?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static (SlidingCaptchaService Service, ICache Cache) CreateService(SlidingCaptchaOptions sliding)
    {
        var provider = BuildProvider();
        var cache = new MemoryCacheService(
            new MemoryCache(new MemoryCacheOptions()),
            Mock.Of<ILogger<MemoryCacheService>>(),
            Microsoft.Extensions.Options.Options.Create(new CachingOptions()),
            provider);
        return (CreateService(provider, cache, sliding), cache);
    }

    private static SlidingCaptchaService CreateService(ICache cache, SlidingCaptchaOptions sliding)
        => CreateService(BuildProvider(), cache, sliding);

    private static SlidingCaptchaService CreateService(IServiceProvider provider, ICache cache, SlidingCaptchaOptions sliding)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new ImagingOptions { SlidingCaptcha = sliding });
        return new SlidingCaptchaService(provider, options, cache);
    }

    private static IServiceProvider BuildProvider()
    {
        var scopedContext = new Mock<IScopedContext>();
        scopedContext.Setup(c => c.ClientIpAddress).Returns(ClientIp);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(scopedContext.Object);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// 失败计数固定为给定值，并记下被写进去的拼图数据。
    /// </summary>
    private sealed class CounterCache
    {
        private readonly Mock<ICache> _mock = new();

        public CounterCache(long counter)
        {
            // GetCounterAsync 是默认接口成员，Moq 不执行默认实现，必须显式 Setup。
            _mock.Setup(c => c.GetCounterAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(counter);

            _mock.Setup(c => c.SetAsync(
                    It.IsAny<string>(), It.IsAny<SlidingCaptchaStoredData>(),
                    It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                .Callback<string, SlidingCaptchaStoredData, TimeSpan?, CancellationToken>(
                    (_, value, _, _) => Stored = value)
                .Returns(Task.CompletedTask);
        }

        public ICache Object => _mock.Object;

        public SlidingCaptchaStoredData? Stored { get; private set; }
    }
}
