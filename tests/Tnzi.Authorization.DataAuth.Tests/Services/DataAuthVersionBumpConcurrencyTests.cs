using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Tnzi.Authorization.DataAuth.Events.Handlers;
using Tnzi.Caching;
using Tnzi.Identity.Events;

namespace Tnzi.Authorization.DataAuth.Tests.Services;

/// <summary>
/// 数据过滤缓存的版本号 bump 必须是原子递增。
/// </summary>
/// <remarks>
/// 读后写（先 <c>GetAsync</c> 再 <c>SetAsync(N + 1)</c>）时，两次并发的 bump 各读到 N、都写回 N+1：
/// 期间按 N+1 编译并缓存的旧规则在缓存期内不再失效，收窄的行级范围最多晚 15 分钟才生效。
/// 下面的替身让两个调用方在「读」处汇合，读后写的实现必然两边读到同一个旧值；原子递增不读，不受影响。
/// </remarks>
public class DataAuthVersionBumpConcurrencyTests
{
    private static readonly Guid UserId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    private static string UserVersionKey => DataAuthService.UserVersionCacheKeyPrefix + UserId;

    private static UserRolesChangedEvent RoleChange() => new()
    {
        UserId = UserId,
        UserName = "user",
        AddedRoleIds = [],
        RemovedRoleIds = [Guid.NewGuid()],
        ChangeType = UserRolesChangeType.Removed,
        ChangedTime = DateTime.UtcNow,
    };

    /// <summary>值存在字典里；<c>GetAsync</c> 在两个读者都到齐之前不返回，模拟两次 bump 交错。</summary>
    private static (Mock<ICache> Cache, ConcurrentDictionary<string, long> Values) InterleavingCache()
    {
        var values = new ConcurrentDictionary<string, long>();
        var arrivals = 0;
        var bothArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new Mock<ICache>();

        cache.Setup(c => c.GetAsync<int>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string key, CancellationToken _) =>
            {
                if (Interlocked.Increment(ref arrivals) >= 2)
                {
                    bothArrived.TrySetResult();
                }

                await bothArrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
                return values.TryGetValue(key, out var value) ? (int)value : 0;
            });
        cache.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .Returns((string key, int value, TimeSpan? _, CancellationToken _) =>
            {
                values[key] = value;
                return Task.CompletedTask;
            });
        cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns((string key, long increment, TimeSpan _, CancellationToken _) =>
                Task.FromResult(values.AddOrUpdate(key, increment, (_, current) => current + increment)));

        return (cache, values);
    }

    [Fact]
    public async Task Two_concurrent_bumps_advance_the_version_by_two()
    {
        var (cache, values) = InterleavingCache();
        values[UserVersionKey] = 5;
        var handler = new UserRolesChangedDataFilterCacheHandler(
            Mock.Of<ILogger<UserRolesChangedDataFilterCacheHandler>>(), cache.Object);

        await Task.WhenAll(
            Task.Run(() => handler.HandleAsync(RoleChange())),
            Task.Run(() => handler.HandleAsync(RoleChange())));

        values[UserVersionKey].ShouldBe(7);
    }

    /// <summary>
    /// 默认内存缓存：写入侧递增存的是 <see cref="long"/>，读取侧必须按同一类型读，否则版本号恒读出 0。
    /// 并发 bump 之后读到的版本恰好前进 2。
    /// </summary>
    [Fact]
    public async Task With_the_memory_cache_the_bumped_version_reads_back_as_written()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        ICache cache = new MemoryCacheService(
            memoryCache,
            Mock.Of<ILogger<MemoryCacheService>>(),
            Microsoft.Extensions.Options.Options.Create(new CachingOptions()));
        var handler = new UserRolesChangedDataFilterCacheHandler(
            Mock.Of<ILogger<UserRolesChangedDataFilterCacheHandler>>(), cache);

        await handler.HandleAsync(RoleChange());
        var before = await cache.GetCounterAsync(UserVersionKey);

        await Task.WhenAll(
            Task.Run(() => handler.HandleAsync(RoleChange())),
            Task.Run(() => handler.HandleAsync(RoleChange())));

        before.ShouldBe(1);
        (await cache.GetCounterAsync(UserVersionKey)).ShouldBe(before + 2);
    }
}
