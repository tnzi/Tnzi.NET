using Microsoft.Extensions.Internal;

namespace Tnzi.Tests.Caching;

/// <summary>
/// 写入时不给过期时间（<c>expiration: null</c>）取 <c>Caching:DefaultExpirationMinutes</c>，<b>绝对过期</b>：
/// 到点就消失，期间被读多少次都一样。与 <c>TrySetAsync</c> / <c>SetManyAsync</c> 和 Redis 实现同一语义。
/// </summary>
/// <remarks>
/// 此前 <c>SetAsync</c> / <c>SetWithTagsAsync</c> 用滑动过期：被反复读的条目永不过期，
/// 同一句调用在内存与 Redis 上寿命不同。
/// </remarks>
public class MemoryCacheServiceDefaultExpirationTests
{
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 10, 11, 8, 0, 0, TimeSpan.Zero));
    private readonly ICache _cache;

    public MemoryCacheServiceDefaultExpirationTests()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions { Clock = _clock });
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_clock);
        _cache = new MemoryCacheService(
            memoryCache,
            Mock.Of<ILogger<MemoryCacheService>>(),
            Microsoft.Extensions.Options.Options.Create(new CachingOptions { DefaultExpirationMinutes = 10 }),
            services.BuildServiceProvider());
    }

    [Fact]
    public async Task SetAsync_WithoutExpiration_ExpiresAtTheDefault_EvenWhenReadRepeatedly()
    {
        await _cache.SetAsync("k", "v");

        for (var minute = 0; minute < 9; minute++)
        {
            _clock.Advance(TimeSpan.FromMinutes(1));
            Assert.Equal("v", await _cache.GetAsync<string>("k"));
        }

        _clock.Advance(TimeSpan.FromMinutes(2));    // t=11，滑动过期下这里仍在

        Assert.Null(await _cache.GetAsync<string>("k"));
    }

    [Fact]
    public async Task SetWithTagsAsync_WithoutExpiration_ExpiresAtTheDefault_EvenWhenReadRepeatedly()
    {
        await _cache.SetWithTagsAsync("k", "v", ["tag"]);

        for (var minute = 0; minute < 9; minute++)
        {
            _clock.Advance(TimeSpan.FromMinutes(1));
            Assert.Equal("v", await _cache.GetAsync<string>("k"));
        }

        _clock.Advance(TimeSpan.FromMinutes(2));

        Assert.Null(await _cache.GetAsync<string>("k"));
    }

    [Fact]
    public async Task SetAsync_WithExplicitExpiration_UsesIt()
    {
        await _cache.SetAsync("k", "v", TimeSpan.FromMinutes(30));

        _clock.Advance(TimeSpan.FromMinutes(20));

        Assert.Equal("v", await _cache.GetAsync<string>("k"));
    }

    private sealed class ManualClock(DateTimeOffset start) : TimeProvider, ISystemClock
    {
        private DateTimeOffset _now = start;

        public DateTimeOffset UtcNow => _now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
