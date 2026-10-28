using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;

namespace Tnzi.Identity.Tests;

/// <summary>
/// 分布式（Redis）会话后端：撤销不能被一次并发的「读 → 改 → 整条写回」抹掉。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ 会话在缓存里是一整条 JSON，撤销、活动续期、刷新续期都是「读出来、改一处、整条写回」。
/// 续期那一方若在撤销之前读、在撤销之后写，写回的是一份 <c>IsRevoked = false</c> 的旧快照：
/// 撤销答了成功、用户索引里也已经删掉了这条会话（之后「登出所有设备」都看不见它），
/// 而手里攥着 access token 的一方每次请求都还能过校验，直到令牌自然过期。
/// 换着两个出口地址发请求就能让每一次请求都走写回路径。
/// </para>
/// <para>
/// 这里用一个能在写入前插一脚的缓存把那次交错稳定地复现出来：续期读完之后、写回之前，另一个实例完成撤销。
/// </para>
/// </remarks>
public class DistributedSessionRevocationRaceTests
{
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36";
    private static readonly SessionOptions Settings = new() { RedisKeyPrefix = "Test:Session" };

    private readonly InterleavingCache _cache = new(new MemoryDistributedCache(Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions())));

    private DistributedSessionService CreateService()
    {
        var serviceProvider = new Mock<IServiceProvider>();
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        serviceProvider.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        return new DistributedSessionService(
            _cache, Microsoft.Extensions.Options.Options.Create(Settings), serviceProvider.Object, UnscopedUserTenantScope.Create());
    }

    private async Task<(DistributedSessionService Requests, DistributedSessionService Admin, Guid SessionId)> SignedInAsync()
    {
        var requests = CreateService();
        var admin = CreateService();
        var sessionId = await requests.CreateSessionAsync(Guid.NewGuid(), "Chrome", "203.0.113.10", UserAgent, DateTime.UtcNow.AddDays(7));
        return (requests, admin, sessionId);
    }

    /// <summary>
    /// ★★★ 每请求校验在地址变化时写回会话；撤销恰好落在它的读与写之间。
    /// 写回之后会话必须仍是已撤销的，这一次请求本身也不该放行。
    /// </summary>
    [Fact]
    public async Task ActivityTouch_ThatReadBeforeARevocation_DoesNotUndoIt()
    {
        var (requests, admin, sessionId) = await SignedInAsync();
        _cache.BeforeNextWriteTo($"{Settings.RedisKeyPrefix}:{sessionId}", () => admin.RevokeSessionAsync(sessionId));

        var validation = await requests.ValidateAsync(sessionId, new SessionValidationContext(UserAgent, "198.51.100.7"));

        Assert.True(_cache.HookFired);
        Assert.Equal(SessionValidationResult.Invalid, validation);
        Assert.False(await requests.IsSessionValidAsync(sessionId));
        Assert.True((await requests.GetSessionAsync(sessionId))!.IsRevoked);
    }

    /// <summary>★★ 刷新时的滑动续期同一形状：续期读完、撤销完成、续期写回。</summary>
    [Fact]
    public async Task RefreshRenewal_ThatReadBeforeARevocation_DoesNotUndoIt()
    {
        var (requests, admin, sessionId) = await SignedInAsync();
        _cache.BeforeNextWriteTo($"{Settings.RedisKeyPrefix}:{sessionId}", () => admin.RevokeSessionAsync(sessionId));

        await requests.RenewSessionAsync(sessionId, DateTime.UtcNow.AddDays(7));

        Assert.True(_cache.HookFired);
        Assert.False(await requests.IsSessionValidAsync(sessionId));
    }

    /// <summary>「登出所有设备」同理：批量撤销落在续期的读与写之间。</summary>
    [Fact]
    public async Task ActivityTouch_ThatReadBeforeRevokeAll_DoesNotUndoIt()
    {
        var requests = CreateService();
        var admin = CreateService();
        var userId = Guid.NewGuid();
        var sessionId = await requests.CreateSessionAsync(userId, "Chrome", "203.0.113.10", UserAgent, DateTime.UtcNow.AddDays(7));
        _cache.BeforeNextWriteTo($"{Settings.RedisKeyPrefix}:{sessionId}", () => admin.RevokeAllSessionsAsync(userId));

        var validation = await requests.ValidateAsync(sessionId, new SessionValidationContext(UserAgent, "198.51.100.7"));

        Assert.True(_cache.HookFired);
        Assert.Equal(SessionValidationResult.Invalid, validation);
        Assert.False(await requests.IsSessionValidAsync(sessionId));
    }

    /// <summary>对照组：没有撤销插进来时，地址变化照常写回、照常放行。</summary>
    [Fact]
    public async Task ActivityTouch_WithoutARevocation_StillValid()
    {
        var (requests, _, sessionId) = await SignedInAsync();

        var validation = await requests.ValidateAsync(sessionId, new SessionValidationContext(UserAgent, "198.51.100.7"));

        Assert.Equal(SessionValidationResult.Valid, validation);
        Assert.True(await requests.IsSessionValidAsync(sessionId));
        Assert.Equal("198.51.100.7", (await requests.GetSessionAsync(sessionId))!.IpAddress);
    }

    /// <summary>在指定键的下一次写入之前跑一次钩子（只跑一次），用来把并发交错固定下来。</summary>
    private sealed class InterleavingCache(IDistributedCache inner) : IDistributedCache
    {
        private string? _watchKey;
        private Func<Task>? _hook;

        public bool HookFired { get; private set; }

        public void BeforeNextWriteTo(string key, Func<Task> hook)
        {
            _watchKey = key;
            _hook = hook;
        }

        public byte[]? Get(string key) => inner.Get(key);

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => inner.GetAsync(key, token);

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => inner.Set(key, value, options);

        public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            if (key == _watchKey && _hook is { } hook)
            {
                _hook = null;
                await hook();
                HookFired = true;
            }

            await inner.SetAsync(key, value, options, token);
        }

        public void Refresh(string key) => inner.Refresh(key);

        public Task RefreshAsync(string key, CancellationToken token = default) => inner.RefreshAsync(key, token);

        public void Remove(string key) => inner.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default) => inner.RemoveAsync(key, token);
    }
}
