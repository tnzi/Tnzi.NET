namespace Tnzi.SignalR.Tests.Services;

/// <summary>
/// <see cref="RateLimitService"/> 对匿名分区的实现。
///
/// ★ 为什么要单独测实现类而不只测过滤器：这几个方法在 <see cref="IRateLimitService"/>
/// 上是**默认接口方法**（为了不让新增打断既有实现），而默认实现是失败关闭的空壳。
/// 实现类若漏掉其中任何一个，编译一样通过、DI 一样解析、过滤器一样调用 —— 只是
/// 匿名连接从此一律被拒。用 Mock 写的过滤器测试看不见这一点。
/// </summary>
public class RateLimitServiceAnonymousTests
{
    private const string Partition = "ip:203.0.113.7";

    private readonly Mock<ICache> _cache = new();
    private readonly RateLimitOptions _options = new()
    {
        Enabled = true,
        MaxConnectionsPerAnonymousPartition = 3,
        MaxMessagesPerMinute = 10,
        BanDuration = TimeSpan.FromMinutes(5),
        AnonymousConnectionCountTtl = TimeSpan.FromHours(2),
    };

    /// <summary>
    /// 刻意返回**接口**类型：这几个方法在接口上带默认实现，实现类漏掉其中任何一个时，
    /// 按具体类调用会编译不过（看起来像测试挡住了），而真实调用点全都走接口 ——
    /// 那时编译通过、DI 解析正常，只是每次都落到失败关闭的空壳上。
    /// 走接口调用才是这批断言真正要覆盖的分派路径。
    /// </summary>
    private IRateLimitService Create()
    {
        var options = new Mock<IOptions<SignalROptions>>();
        options.Setup(o => o.Value).Returns(new SignalROptions { RateLimit = _options });
        return new RateLimitService(
            _cache.Object,
            new ConnectionManager(Mock.Of<ILogger<ConnectionManager>>()),
            options.Object,
            Mock.Of<ILogger<RateLimitService>>());
    }

    private static string ConnectionKey => CacheKeys.SignalR.AnonymousConnectionCount(Partition);

    [Fact]
    public async Task TryAcquire_AllowsWhileUnderTheCap()
    {
        _cache.Setup(c => c.IncrementAsync(ConnectionKey, 1, _options.AnonymousConnectionCountTtl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        (await Create().TryAcquireAnonymousConnectionAsync(Partition)).ShouldBeTrue();
    }

    /// <summary>
    /// ★ 先占后判：占不下时必须把刚加的那一份退回来，否则被拒的连接会永久
    /// 吃掉一个名额，反复重连就能把一个分区自己锁死。
    /// </summary>
    [Fact]
    public async Task TryAcquire_RejectsOverTheCapAndGivesTheSlotBack()
    {
        _cache.Setup(c => c.IncrementAsync(ConnectionKey, 1, _options.AnonymousConnectionCountTtl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);
        var service = Create();

        (await service.TryAcquireAnonymousConnectionAsync(Partition)).ShouldBeFalse();

        _cache.Verify(c => c.DecrementAsync(ConnectionKey, 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Release_DecrementsTheCount()
    {
        _cache.Setup(c => c.DecrementAsync(ConnectionKey, 1, It.IsAny<CancellationToken>())).ReturnsAsync(2);

        await Create().ReleaseAnonymousConnectionAsync(Partition);

        _cache.Verify(c => c.DecrementAsync(ConnectionKey, 1, It.IsAny<CancellationToken>()), Times.Once);
        _cache.Verify(c => c.RemoveAsync(ConnectionKey, It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// TTL 过期后计数从 0 起算，此后的断开会把它推成负数。负数等于给该分区
    /// 一份远超上限的额度，所以清掉整条。
    /// </summary>
    [Fact]
    public async Task Release_ClearsTheCounterWhenItGoesNegative()
    {
        _cache.Setup(c => c.DecrementAsync(ConnectionKey, 1, It.IsAny<CancellationToken>())).ReturnsAsync(-1);

        await Create().ReleaseAnonymousConnectionAsync(Partition);

        _cache.Verify(c => c.RemoveAsync(ConnectionKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MessageRate_UsesTheCounterApiNotTypedGet()
    {
        var key = CacheKeys.SignalR.AnonymousMessageRateCount(Partition);
        _cache.Setup(c => c.GetCounterAsync(key, It.IsAny<CancellationToken>())).ReturnsAsync(9);

        (await Create().CheckAnonymousMessageRateLimitAsync(Partition)).ShouldBeTrue();

        _cache.Setup(c => c.GetCounterAsync(key, It.IsAny<CancellationToken>())).ReturnsAsync(10);
        (await Create().CheckAnonymousMessageRateLimitAsync(Partition)).ShouldBeFalse();
    }

    [Fact]
    public async Task RecordMessage_IncrementsWithAOneMinuteWindow()
    {
        var key = CacheKeys.SignalR.AnonymousMessageRateCount(Partition);

        await Create().RecordAnonymousMessageAsync(Partition);

        _cache.Verify(
            c => c.IncrementAsync(key, 1, TimeSpan.FromMinutes(1), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task BanAndCheckBan_UseTheAnonymousKeyspace()
    {
        var key = CacheKeys.SignalR.AnonymousBan(Partition);
        var service = Create();

        await service.BanAnonymousAsync(Partition, TimeSpan.FromMinutes(5));
        _cache.Verify(
            c => c.SetAsync(key, true, TimeSpan.FromMinutes(5), It.IsAny<CancellationToken>()),
            Times.Once);

        _cache.Setup(c => c.ExistsAsync(key, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        (await service.IsAnonymousBannedAsync(Partition)).ShouldBeTrue();
    }

    /// <summary>
    /// 匿名与按用户的键空间必须分开：一个恰好长得像 GUID 的分区键不该与真实用户
    /// 撞到同一个计数或封禁标记上。
    /// </summary>
    [Fact]
    public void AnonymousKeyspaceIsDisjointFromTheUserKeyspace()
    {
        var id = Guid.NewGuid();

        CacheKeys.SignalR.AnonymousBan(id.ToString())
            .ShouldNotBe(CacheKeys.SignalR.UserBan(id));
        CacheKeys.SignalR.AnonymousMessageRateCount(id.ToString())
            .ShouldNotBe(CacheKeys.SignalR.MessageRateCount(id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankPartitionKeysAreRejected(string partitionKey)
    {
        var service = Create();

        await Should.ThrowAsync<ArgumentException>(() => service.TryAcquireAnonymousConnectionAsync(partitionKey));
        await Should.ThrowAsync<ArgumentException>(() => service.CheckAnonymousMessageRateLimitAsync(partitionKey));
    }
}
