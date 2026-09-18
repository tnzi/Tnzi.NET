using Microsoft.Extensions.Internal;

namespace Tnzi.Tests.Caching;

/// <summary>
/// <c>IncrementAsync(key, increment, expiration)</c> 是固定窗口：过期时刻在键首次创建时定下，
/// 之后的递增沿用它，而不是每次把 TTL 往后推。
/// </summary>
/// <remarks>
/// 每次重设 TTL = 滑动惩罚窗口：限流配置 100 次 / 60 秒，客户端超限后每次重试都把窗口续到 60 秒之后，
/// 只要它以任何小于 60 秒的间隔重试，计数器永不过期 —— 被限流的调用方再也恢复不了，与配置的窗口长度无关。
/// <c>RateLimitService</c> 与「每日登录次数」这类调用方按「仅首次创建时设过期」实现，两个缓存实现此前都不符合。
/// 用假时钟同时驱动 <see cref="MemoryCache"/> 与 <see cref="MemoryCacheService"/>，窗口边界才能精确断言。
/// </remarks>
public class MemoryCacheServiceCounterWindowTests
{
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.Zero));
    private readonly ICache _cache;

    public MemoryCacheServiceCounterWindowTests()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions { Clock = _clock });
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_clock);
        _cache = new MemoryCacheService(
            memoryCache,
            Mock.Of<ILogger<MemoryCacheService>>(),
            Microsoft.Extensions.Options.Options.Create(new CachingOptions()),
            services.BuildServiceProvider());
    }

    [Fact]
    public async Task IncrementAsync_KeepsTheOriginalExpiry_OnSubsequentIncrements()
    {
        const string key = "window";
        var window = TimeSpan.FromSeconds(10);

        await _cache.IncrementAsync(key, 1, window);      // t=0，窗口到 t=10
        _clock.Advance(TimeSpan.FromSeconds(8));
        await _cache.IncrementAsync(key, 1, window);      // t=8，滑动语义会把窗口续到 t=18
        Assert.Equal(2L, await _cache.GetCounterAsync(key));

        _clock.Advance(TimeSpan.FromSeconds(3));          // t=11

        // 固定窗口在 t=10 结束：计数归零；滑动语义下这里仍是 2
        Assert.Equal(0L, await _cache.GetCounterAsync(key));
    }

    [Fact]
    public async Task IncrementAsync_StartsANewWindow_AfterTheOldOneExpired()
    {
        const string key = "window";
        var window = TimeSpan.FromSeconds(10);

        await _cache.IncrementAsync(key, 1, window);      // 窗口 1：t=0..10
        _clock.Advance(TimeSpan.FromSeconds(11));

        Assert.Equal(1L, await _cache.IncrementAsync(key, 1, window)); // 窗口 2：t=11..21，从 1 重新计
        _clock.Advance(TimeSpan.FromSeconds(9));                       // t=20，仍在窗口 2 内
        Assert.Equal(1L, await _cache.GetCounterAsync(key));
        _clock.Advance(TimeSpan.FromSeconds(2));                       // t=22
        Assert.Equal(0L, await _cache.GetCounterAsync(key));
    }

    [Fact]
    public async Task IncrementAsync_AfterRemove_StartsAFreshWindow()
    {
        const string key = "window";
        var window = TimeSpan.FromSeconds(10);

        await _cache.IncrementAsync(key, 1, window);      // 窗口到 t=10
        _clock.Advance(TimeSpan.FromSeconds(5));
        await _cache.RemoveAsync(key);                    // 清掉失败记录（2FA 验证成功那种）
        await _cache.IncrementAsync(key, 1, window);      // t=5，新窗口到 t=15

        _clock.Advance(TimeSpan.FromSeconds(7));          // t=12：旧窗口早过了，新窗口还在
        Assert.Equal(1L, await _cache.GetCounterAsync(key));
    }

    [Fact]
    public void CounterWindow_IdentityIsByReference_NotByExpiry()
    {
        // 窗口记录按引用相等撤销：删除的驱逐回调是线程池派发的，迟到时新窗口早已登记；
        // 两个窗口在同一时钟读数内创建 ⇒ ExpiresAt 逐字相同，按值相等（record）它们会互换身份，
        // 迟到的回调摘掉活着的新窗口，下一次递增又开一个新窗口、过期时刻被往后推。
        // 回调时序在测试里控制不了，所以直接钉住类型的相等语义。
        var windowType = typeof(MemoryCacheService).GetNestedType("CounterWindow", BindingFlags.NonPublic);
        Assert.NotNull(windowType);

        var expiresAt = new DateTimeOffset(2026, 9, 12, 8, 0, 10, TimeSpan.Zero);
        var first = Activator.CreateInstance(windowType, expiresAt)!;
        var second = Activator.CreateInstance(windowType, expiresAt)!;

        Assert.False(first.Equals(second), "two windows with the same expiry must not be interchangeable");
        Assert.True(first.Equals(first));
        Assert.Null(windowType.GetProperty("EqualityContract", BindingFlags.NonPublic | BindingFlags.Instance));
    }

    /// <summary>
    /// 同一枚时钟同时喂给 <see cref="MemoryCache"/>（判过期）和 <see cref="MemoryCacheService"/>（算过期时刻）。
    /// </summary>
    private sealed class ManualClock(DateTimeOffset start) : TimeProvider, ISystemClock
    {
        private DateTimeOffset _now = start;

        public DateTimeOffset UtcNow => _now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
