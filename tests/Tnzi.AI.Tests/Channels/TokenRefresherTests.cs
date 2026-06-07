using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.AI.Channels.Adapters;

namespace Tnzi.AI.Tests.Channels;

public class TokenRefresherTests
{
    [Fact]
    public async Task GetTokenAsync_FirstCall_InvokesRefreshDelegate()
    {
        var callCount = 0;
        await using var refresher = new TokenRefresher(
            async ct =>
            {
                callCount++;
                await Task.Yield();
                return ("token-1", 7200);
            },
            NullLogger.Instance,
            adapterName: "test");

        var token = await refresher.GetTokenAsync(CancellationToken.None);

        token.ShouldBe("token-1");
        callCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetTokenAsync_CalledTwiceWithinTtl_RefreshesOnce()
    {
        var callCount = 0;
        await using var refresher = new TokenRefresher(
            async ct =>
            {
                callCount++;
                await Task.Yield();
                return ("token-cached", 7200);
            },
            NullLogger.Instance,
            adapterName: "test");

        var t1 = await refresher.GetTokenAsync(CancellationToken.None);
        var t2 = await refresher.GetTokenAsync(CancellationToken.None);

        t1.ShouldBe("token-cached");
        t2.ShouldBe("token-cached");
        callCount.ShouldBe(1); // 缓存命中，delegate 只调用一次
    }

    [Fact]
    public async Task GetTokenAsync_ConcurrentCalls_RefreshesOnce()
    {
        var callCount = 0;
        await using var refresher = new TokenRefresher(
            async ct =>
            {
                Interlocked.Increment(ref callCount);
                await Task.Delay(10, ct); // 模拟网络延迟
                return ("token-concurrent", 7200);
            },
            NullLogger.Instance,
            adapterName: "test");

        // 并发发起 8 个请求
        var tasks = Enumerable.Range(0, 8)
            .Select(_ => refresher.GetTokenAsync(CancellationToken.None))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        results.ShouldAllBe(t => t == "token-concurrent");
        callCount.ShouldBe(1); // 双重检查锁确保只刷新一次
    }

    [Fact]
    public async Task GetTokenAsync_ExpiredToken_RefreshesAgain()
    {
        var callCount = 0;
        // earlyRenewSeconds=0 + expireIn=1 → 1 秒后立即过期
        await using var refresher = new TokenRefresher(
            async ct =>
            {
                callCount++;
                await Task.Yield();
                return ($"token-{callCount}", expireIn: 1);
            },
            NullLogger.Instance,
            adapterName: "test",
            earlyRenewSeconds: 0);

        var t1 = await refresher.GetTokenAsync(CancellationToken.None);
        t1.ShouldBe("token-1");

        // 等待 token 过期
        await Task.Delay(1100);

        var t2 = await refresher.GetTokenAsync(CancellationToken.None);
        t2.ShouldBe("token-2");
        callCount.ShouldBe(2);
    }

    [Fact]
    public async Task GetTokenAsync_RefreshThrows_PropagatesException()
    {
        await using var refresher = new TokenRefresher(
            _ => throw new HttpRequestException("Simulated API failure"),
            NullLogger.Instance,
            adapterName: "test");

        await Should.ThrowAsync<HttpRequestException>(
            () => refresher.GetTokenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DisposeAsync_ReleasesResources()
    {
        var refresher = new TokenRefresher(
            async ct => { await Task.Yield(); return ("t", 3600); },
            NullLogger.Instance,
            adapterName: "test");

        await refresher.DisposeAsync();

        // 再次 Dispose 不应抛异常（幂等）
        // SemaphoreSlim.Dispose 后再访问会抛 ObjectDisposedException，
        // 此处只验证 DisposeAsync 本身不抛
    }
}
