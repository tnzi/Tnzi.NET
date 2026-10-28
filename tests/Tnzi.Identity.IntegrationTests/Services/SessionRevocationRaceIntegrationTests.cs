using Tnzi.Data;
using Tnzi.Identity.Services;

namespace Tnzi.Identity.IntegrationTests.Services;

/// <summary>
/// 数据库会话后端：撤销不能被续期写回、也不能被一份撤销之前读到的缓存快照抹掉。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ 两种写回都会把撤销抹掉。刷新续期此前读出整行、改两列、再整行写回（<c>UpdateAsync</c> 把实体标成
/// <c>Modified</c>，所有列都写），撤销落在它的读与写之间就被 <c>IsRevoked = false</c> 盖掉；
/// 每请求校验的活动续期本身只写两列，但随后把<b>撤销之前</b>读到的快照重新放回校验缓存，
/// 换着两个出口地址发请求，每一次都会把那份旧快照续上一个缓存周期。
/// </para>
/// <para>跑真实关系型 provider：要守的正是那条带条件的 <c>UPDATE</c> 真的不碰已撤销的行。</para>
/// </remarks>
public class SessionRevocationRaceIntegrationTests : RelationalIdentityIntegrationTestBase
{
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36";

    private DatabaseSessionService CreateService() => new(CreateRepository<UserSession>(), ServiceProvider, new UserTenantScopeProvider(CreateRepository<User>()));

    private async Task<UserSession> SeedSessionAsync(Guid userId)
    {
        var session = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            UserAgent = UserAgent,
            IpAddress = "203.0.113.10",
            CreationTime = DateTime.UtcNow.AddHours(-1),
            LastActivityTime = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            IsRevoked = false,
        };
        DbContext.UserSessions.Add(session);
        await SaveChangesAsync();
        return session;
    }

    /// <summary>撤销在另一个连接上提交：不经本实例的缓存失效，正是「本请求的快照读在撤销之前」的样子。</summary>
    private Task RevokeBehindTheServicesBackAsync(Guid sessionId)
        => DbContext.UserSessions.Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsRevoked, true).SetProperty(x => x.RevokedAt, DateTime.UtcNow));

    private Task<bool> IsRevokedInTheDatabaseAsync(Guid sessionId)
        => DbContext.UserSessions.AsNoTracking().Where(s => s.Id == sessionId).Select(s => s.IsRevoked).SingleAsync();

    /// <summary>
    /// ★★★ 校验缓存里是撤销之前的快照，请求换了出口地址（于是要写回）：这一次必须判失效、把旧快照清掉，
    /// 下一次（地址又换回来）也不能再被那份快照放行。
    /// </summary>
    [Fact]
    public async Task Touch_OnARowRevokedAfterItsSnapshotWasCached_IsInvalid_AndEvictsTheSnapshot()
    {
        var user = await CreateUserAsync();
        var session = await SeedSessionAsync(user.Id);
        var service = CreateService();
        Assert.Equal(SessionValidationResult.Valid, await service.ValidateAsync(session.Id, new SessionValidationContext(UserAgent, "203.0.113.10")));

        await RevokeBehindTheServicesBackAsync(session.Id);

        Assert.Equal(SessionValidationResult.Invalid, await service.ValidateAsync(session.Id, new SessionValidationContext(UserAgent, "198.51.100.7")));
        Assert.Equal(SessionValidationResult.Invalid, await service.ValidateAsync(session.Id, new SessionValidationContext(UserAgent, "203.0.113.10")));
        Assert.True(await IsRevokedInTheDatabaseAsync(session.Id));
    }

    /// <summary>
    /// ★★★ 刷新续期不得把已撤销的行写回成未撤销。行在本 DbContext 里被跟踪着、撤销在别处提交，
    /// 整行写回的实现会把内存里那份 <c>IsRevoked = false</c> 原样落库。
    /// </summary>
    [Fact]
    public async Task Renew_DoesNotWriteARevokedRowBackToActive()
    {
        var user = await CreateUserAsync();
        var session = await SeedSessionAsync(user.Id);
        await RevokeBehindTheServicesBackAsync(session.Id);

        await CreateService().RenewSessionAsync(session.Id, DateTime.UtcNow.AddDays(7));

        Assert.True(await IsRevokedInTheDatabaseAsync(session.Id));
        Assert.False(await CreateService().IsSessionValidAsync(session.Id));
    }

    /// <summary>对照组：没被撤销的会话照常续期（活动时间与过期时间都往后推）。</summary>
    [Fact]
    public async Task Renew_OnAnActiveSession_ExtendsIt()
    {
        var user = await CreateUserAsync();
        var session = await SeedSessionAsync(user.Id);
        var newExpiry = DateTime.UtcNow.AddDays(14);

        var result = await CreateService().RenewSessionAsync(session.Id, newExpiry);

        Assert.True(result.Succeeded);
        var row = await DbContext.UserSessions.AsNoTracking().SingleAsync(s => s.Id == session.Id);
        Assert.False(row.IsRevoked);
        Assert.Equal(newExpiry, row.ExpiresAt!.Value, TimeSpan.FromSeconds(1));
    }

    /// <summary>管理端「刷新活动时间」同一形状：只在行未撤销时写，已撤销照旧答失败。</summary>
    [Fact]
    public async Task UpdateActivityTime_TouchesAnActiveSession_AndRefusesARevokedOne()
    {
        var user = await CreateUserAsync();
        var active = await SeedSessionAsync(user.Id);
        var revoked = await SeedSessionAsync(user.Id);
        await RevokeBehindTheServicesBackAsync(revoked.Id);
        var before = DateTime.UtcNow.AddSeconds(-1);

        var touched = await CreateService().UpdateActivityTimeAsync(active.Id);
        var refused = await CreateService().UpdateActivityTimeAsync(revoked.Id);

        Assert.True(touched.Succeeded);
        Assert.True((await DbContext.UserSessions.AsNoTracking().SingleAsync(s => s.Id == active.Id)).LastActivityTime >= before);
        Assert.False(refused.Succeeded);
        Assert.True(await IsRevokedInTheDatabaseAsync(revoked.Id));
    }

    /// <summary>
    /// ★★ 在事务里撤销时，校验缓存在提交之后要再清一次：提交之前清掉的那一刻到提交之间，
    /// 别的请求读到的还是未撤销的行，会把它重新放回缓存。
    /// </summary>
    [Fact]
    public async Task Revoke_InsideATransaction_ClearsTheValidityCacheAgainAfterCommit()
    {
        var user = await CreateUserAsync();
        var session = await SeedSessionAsync(user.Id);
        var service = CreateService();
        var ambient = new CapturingAmbientScope();
        var previous = AmbientUnitOfWork.Current;
        AmbientUnitOfWork.Set(ambient);
        try
        {
            Assert.True((await service.RevokeSessionAsync(session.Id)).Succeeded);
        }
        finally
        {
            AmbientUnitOfWork.Set(previous);
        }

        // 提交之前，另一个请求读到旧行并把它放回了缓存。
        var concurrentReader = CreateService();
        await RevokeUndoneForAReaderAsync(session.Id, concurrentReader);

        foreach (var postCommit in ambient.Queued)
        {
            await postCommit(CancellationToken.None);
        }

        Assert.NotEmpty(ambient.Queued);
        Assert.False(await CreateService().IsSessionValidAsync(session.Id));
    }

    /// <summary>让一个读者在「撤销尚未提交」时把未撤销的快照放进缓存。</summary>
    private async Task RevokeUndoneForAReaderAsync(Guid sessionId, DatabaseSessionService reader)
    {
        await DbContext.UserSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsRevoked, false));
        Assert.True(await reader.IsSessionValidAsync(sessionId));
        await DbContext.UserSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsRevoked, true));
    }

    private sealed class CapturingAmbientScope : IAmbientUnitOfWorkScope
    {
        public List<Func<CancellationToken, Task>> Queued { get; } = [];

        public bool IsTransactionActive => true;

        public void EnqueuePostCommit(Func<CancellationToken, Task> action) => Queued.Add(action);
    }
}
