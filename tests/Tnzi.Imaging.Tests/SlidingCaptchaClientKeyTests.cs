using Microsoft.Extensions.DependencyInjection;
using Tnzi.ScopedContext;

namespace Tnzi.Imaging.Tests;

/// <summary>
/// 自适应难度按谁计数。
/// </summary>
/// <remarks>
/// <para>
/// <b>被保护的缺陷</b>：难度取决于"这个客户端失败过几次"，而那个标识此前是
/// <c>[FromQuery] clientId</c> —— 由调用方自报。省略它或每次换一个随机值，
/// 失败计数永远是 0、<b>永远拿到最低难度</b>，暴力破解连一次难度上调都不会遇到；
/// 填别人的值则能把对方顶到最高难度。两种滥用都不需要任何凭据。
/// </para>
/// <para>
/// ★ 修法不是"校验这个参数"，而是<b>把参数删掉</b>：只要它还在签名里，
/// 就一定会有人把请求里的值转发进来。现在标识由服务端从当前作用域派生。
/// </para>
/// </remarks>
public class SlidingCaptchaClientKeyTests
{
    private const string ClientIp = "203.0.113.7";

    /// <summary>
    /// 同一个 IP 累计的失败次数会真的把难度顶上去。
    /// </summary>
    [Fact]
    public async Task FailuresRecordedForTheCallerIp_RaiseTheDifficulty()
    {
        var cache = new RecordingCache(9);
        var service = CreateService(cache, ClientIp);

        var result = await service.GenerateAdaptiveAsync();

        result.Succeeded.ShouldBeTrue();
        cache.Stored.ShouldNotBeNull();
        cache.Stored!.Tolerance.ShouldBe(2, "失败 9 次应当落到最高难度那一档");
    }

    /// <summary>
    /// 没有失败历史时是普通难度 —— 防止把守卫做成"永远最难"。
    /// </summary>
    [Fact]
    public async Task WithoutFailures_TheDifficultyStaysOrdinary()
    {
        var cache = new RecordingCache(0);
        var service = CreateService(cache, ClientIp);

        await service.GenerateAdaptiveAsync();

        cache.Stored!.Tolerance.ShouldBe(5);
    }

    /// <summary>
    /// 计数键从 IP 派生，且<b>不是</b> IP 原文。
    /// </summary>
    /// <remarks>
    /// 键会进缓存，而缓存可能是多应用共享的 Redis：没必要把可识别的地址摊在那里。
    /// </remarks>
    [Fact]
    public async Task TheFailureCounterKey_IsDerivedFromTheIpAndDoesNotContainIt()
    {
        var cache = new RecordingCache(1);
        var service = CreateService(cache, ClientIp);

        await service.GenerateAdaptiveAsync();

        cache.CounterKey.ShouldNotBeNull();
        cache.CounterKey.ShouldStartWith("captcha:failures:");
        cache.CounterKey.ShouldNotContain(ClientIp, Case.Sensitive);
    }

    /// <summary>
    /// 不同 IP 计到不同的键上，否则所有人共用一个计数、互相推高难度。
    /// </summary>
    [Fact]
    public async Task DifferentCallers_CountAgainstDifferentKeys()
    {
        var first = new RecordingCache(1);
        await CreateService(first, "198.51.100.1").GenerateAdaptiveAsync();

        var second = new RecordingCache(1);
        await CreateService(second, "198.51.100.2").GenerateAdaptiveAsync();

        first.CounterKey.ShouldNotBe(second.CounterKey);
    }

    /// <summary>
    /// 取不到客户端 IP 时按「没有失败历史」处理，而不是把所有人挤进同一个键。
    /// </summary>
    /// <remarks>
    /// 挤进同一个键会让互不相干的请求互相推高难度 —— 一次配置缺失就变成一次拒绝服务。
    /// </remarks>
    [Fact]
    public async Task WithoutAClientIp_NoFailureCounterIsConsulted()
    {
        var cache = new RecordingCache(9);
        var service = CreateService(cache, clientIp: null);

        await service.GenerateAdaptiveAsync();

        cache.CounterKey.ShouldBeNull("没有 IP 就没有计数键，也就不该去查失败次数");
        cache.Stored!.Tolerance.ShouldBe(5);
    }

    private static SlidingCaptchaService CreateService(RecordingCache cache, string? clientIp)
    {
        var scopedContext = new Mock<IScopedContext>();
        scopedContext.Setup(c => c.ClientIpAddress).Returns(clientIp);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(scopedContext.Object);
        var provider = services.BuildServiceProvider();

        var options = Microsoft.Extensions.Options.Options.Create(new ImagingOptions());

        return new SlidingCaptchaService(provider, options, cache.Object);
    }

    /// <summary>
    /// 只记录被写进去的东西：断言的是「服务端决定了什么」，不是缓存怎么实现。
    /// </summary>
    private sealed class RecordingCache
    {
        private readonly Mock<ICache> _mock = new();

        public RecordingCache(long counter)
        {
            // ★ GetCounterAsync 是默认接口成员，Moq 不会执行它的默认实现（会直接返回 default），
            // 因此必须显式 Setup —— 不 Setup 的话计数恒为 0，这几条测试会安静地测不到东西。
            _mock.Setup(c => c.GetCounterAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, CancellationToken>((key, _) => CounterKey = key)
                .ReturnsAsync(counter);

            _mock.Setup(c => c.SetAsync(
                    It.IsAny<string>(), It.IsAny<SlidingCaptchaStoredData>(),
                    It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                .Callback<string, SlidingCaptchaStoredData, TimeSpan?, CancellationToken>(
                    (_, value, _, _) => Stored = value)
                .Returns(Task.CompletedTask);
        }

        public ICache Object => _mock.Object;

        public string? CounterKey { get; private set; }

        public SlidingCaptchaStoredData? Stored { get; private set; }
    }
}
