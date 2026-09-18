using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;

namespace Tnzi.AspNetCore.Tests.Security;

/// <summary>
/// 固定窗口限流对着<b>真实</b>的 <see cref="MemoryCacheService"/> 断言窗口语义。
/// </summary>
/// <remarks>
/// <see cref="RateLimitServiceTests"/> 全部 <c>Mock&lt;ICache&gt;</c>，只验证过期参数被传入，钉不住窗口到底会不会滚动。
/// 此前两个缓存实现都在每次递增时重设 TTL：超限后持续重试的客户端把窗口一直续下去，
/// 只有严格等满整整一个 <c>Retry-After</c> 的客户端才能恢复 —— 而客户端一定会重试。
/// </remarks>
public class RateLimitFixedWindowTests
{
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.Zero));
    private readonly RateLimitService _service;

    public RateLimitFixedWindowTests()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions { Clock = _clock });
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_clock);
        var cache = new MemoryCacheService(
            memoryCache,
            Mock.Of<ILogger<MemoryCacheService>>(),
            Microsoft.Extensions.Options.Options.Create(new CachingOptions()),
            services.BuildServiceProvider());
        _service = new RateLimitService(cache);
    }

    [Fact]
    public async Task FixedWindow_ResetsAfterTheWindow_EvenUnderContinuousRequests()
    {
        const int windowSeconds = 60;

        // 客户端每秒打一次，打满 59 秒：计数一路涨到 60，窗口从第一次命中起算
        long last = 0;
        for (var second = 0; second < 60; second++)
        {
            last = await _service.IncrementAndGetAsync("client", windowSeconds);
            _clock.Advance(TimeSpan.FromSeconds(1));
        }
        Assert.Equal(60, last);

        // t=60：窗口结束，即使前一秒还在请求，计数也必须从 1 重新开始。
        // 滑动语义下这里是 61 —— 只要客户端不停，它就永远出不来
        Assert.Equal(1, await _service.IncrementAndGetAsync("client", windowSeconds));
    }

    private sealed class ManualClock(DateTimeOffset start) : TimeProvider, ISystemClock
    {
        private DateTimeOffset _now = start;

        public DateTimeOffset UtcNow => _now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
