using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Tnzi.Caching;
using Tnzi.HealthChecks.Checks;

namespace Tnzi.HealthChecks.Tests;

/// <summary>
/// 缓存与 Redis 健康检查写读删一个测试键。缓存在多实例间共享，/health 与 /health/ready 又各自跑全部检查：
/// 用固定键时，另一次检查在本次「写」与「读」之间写入自己的值、或删掉本次的值，本次就报 Degraded。
/// 这里在本次写入之后确定性地插入一整次另一实例的检查，模拟那种交错。
/// </summary>
public class SharedCacheProbeKeyTests
{
    [Fact]
    public async Task CacheCheck_IsNotDisturbedByAnotherInstanceCheckingTheSameCache()
    {
        var store = new ConcurrentDictionary<string, object?>();
        Func<Task>? interleave = null;
        var cache = new Mock<ICache>();
        cache.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .Returns(async (string key, string value, TimeSpan? _, CancellationToken _) =>
            {
                store[key] = value;
                var other = interleave;
                interleave = null;
                if (other != null) await other();
            });
        cache.Setup(c => c.GetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) => store.TryGetValue(key, out var v) ? (string?)v : null);
        cache.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string key, CancellationToken ct) => { store.TryRemove(key, out _); return Task.CompletedTask; });
        cache.Setup(c => c.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) => store.ContainsKey(key));

        var thisInstance = new CacheHealthCheck(cache.Object);
        var otherInstance = new CacheHealthCheck(cache.Object);
        HealthCheckResult? otherResult = null;
        interleave = async () => otherResult = await otherInstance.CheckHealthAsync(new HealthCheckContext());

        var result = await thisInstance.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(HealthStatus.Healthy, otherResult?.Status);
        Assert.Empty(store);
    }

    [Fact]
    public async Task RedisCheck_IsNotDisturbedByAnotherInstanceCheckingTheSameCache()
    {
        var cache = new InterleavingDistributedCache();
        var thisInstance = new RedisHealthCheck(cache);
        var otherInstance = new RedisHealthCheck(cache);
        HealthCheckResult? otherResult = null;
        cache.Interleave = async () => otherResult = await otherInstance.CheckHealthAsync(new HealthCheckContext());

        var result = await thisInstance.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(HealthStatus.Healthy, otherResult?.Status);
        Assert.Empty(cache.Store);
    }

    /// <summary>共享的分布式缓存；第一次写入之后插入一次 <see cref="Interleave"/>。</summary>
    private sealed class InterleavingDistributedCache : IDistributedCache
    {
        public ConcurrentDictionary<string, byte[]> Store { get; } = new();
        public Func<Task>? Interleave { get; set; }

        public byte[]? Get(string key) => Store.TryGetValue(key, out var v) ? v : null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => Store[key] = value;

        public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Store[key] = value;
            var other = Interleave;
            Interleave = null;
            if (other != null) await other();
        }

        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) => Store.TryRemove(key, out _);
        public Task RemoveAsync(string key, CancellationToken token = default) { Remove(key); return Task.CompletedTask; }
    }
}
