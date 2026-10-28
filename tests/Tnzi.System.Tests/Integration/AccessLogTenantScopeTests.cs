using Tnzi.Data;
using Tnzi.MultiTenancy;

namespace Tnzi.System.Tests.Integration;

/// <summary>
/// 访问日志的读取与删除按<b>调用者的租户</b>收口：租户内的调用者只看得到、只删得到本租户的行，宿主看全部。
/// </summary>
/// <remarks>
/// <para>
/// <c>Sys_AccessLog</c> 自 <c>AccessLogMiddleware</c> 起记下每个租户每一次请求的用户名、来源地址与路径。
/// 此前这张表没有租户列、读取面一处租户谓词都没有：多租户部署里持 <c>system.accessLog.view</c> 的租户管理员
/// 看得到别家用户的名字、IP 与访问路径；持 <c>system.accessLog.delete</c> 的一次 <c>DELETE expired?days=1</c>
/// 就抹掉全部租户的访问日志。
/// </para>
/// <para>
/// ★ 谓词以多租户开关为闸门：<c>TenantId</c> 在多租户关闭时被 Ignore，对它写谓词会在查询翻译时抛异常。
/// 本类的 DbContext 以 <c>MultiTenancy:Enabled=true</c> 构造（否则列不存在）；
/// <see cref="AccessLogSingleTenantTests"/> 在默认（关闭）的 DbContext 上守另一半。
/// </para>
/// </remarks>
public class AccessLogTenantScopeTests : IntegrationTestBase
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid UserOfA = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid UserOfB = Guid.Parse("22222222-0000-0000-0000-000000000002");

    protected override void ConfigureServices(IServiceCollection services)
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
        // 多租户开启：TenantId 列才会被映射并建索引（见 AccessLogConfiguration）。
        services.Configure<MultiTenancyOptions>(o => o.Enabled = true);
    }

    public AccessLogTenantScopeTests()
    {
        var now = DateTime.UtcNow;
        DbContext.AccessLogs.AddRange(
            Log("/api/a/one", TenantA, UserOfA, "alice", now.AddHours(-1)),
            Log("/api/a/two", TenantA, UserOfA, "alice", now.AddHours(-2)),
            Log("/api/b/one", TenantB, UserOfB, "bob", now.AddHours(-1)),
            Log("/api/host/one", null, null, null, now.AddHours(-1)),
            Log("/api/a/old", TenantA, UserOfA, "alice", now.AddDays(-400)),
            Log("/api/b/old", TenantB, UserOfB, "bob", now.AddDays(-400)));
        DbContext.SaveChangesAsync().GetAwaiter().GetResult();
        DbContext.ChangeTracker.Clear();
    }

    private static AccessLog Log(string path, Guid? tenantId, Guid? userId, string? userName, DateTime when) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        UserId = userId,
        UserName = userName,
        Path = path,
        Method = "GET",
        IpAddress = "203.0.113.7",
        StatusCode = path.EndsWith("two", StringComparison.Ordinal) ? 500 : 200,
        ResponseTime = 10,
        CreationTime = when,
    };

    private static ICurrentTenant Tenant(Guid? tenantId)
    {
        var tenant = new Mock<ICurrentTenant>();
        tenant.Setup(t => t.Id).Returns(tenantId);
        tenant.Setup(t => t.IsAvailable).Returns(tenantId.HasValue);
        return tenant.Object;
    }

    private sealed class CapturingSender : IAccessLogSender
    {
        public List<AccessLogDto> Captured { get; } = [];

        public Task SendAsync(AccessLogDto log)
        {
            Captured.Add(log);
            return Task.CompletedTask;
        }
    }

    /// <param name="tenantId">调用者的租户；null 为宿主。</param>
    /// <param name="sender">后台队列的替身；不给则用一个只记录的。</param>
    private AccessLogService Service(Guid? tenantId, IAccessLogSender? sender = null)
        => new(
            ServiceProvider,
            new EFCoreRepository<SystemTestDbContext, AccessLog, Guid>(DbContext, serviceProvider: ServiceProvider),
            sender ?? new CapturingSender(),
            accessLogOptions: null,
            currentTenant: Tenant(tenantId),
            multiTenancyOptions: Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions { Enabled = true }));

    private static string[] Paths(Result<IPagedList<AccessLogDto>> result)
    {
        result.Succeeded.ShouldBeTrue(result.Message);
        return result.Data!.Items.Select(l => l.Path).OrderBy(p => p, StringComparer.Ordinal).ToArray();
    }

    // ---- 读取 -------------------------------------------------------------------

    [Fact]
    public async Task TenantCaller_List_SeesOnlyOwnTenantRows()
    {
        Paths(await Service(TenantA).GetAccessLogsAsync(new AccessLogQueryDto()))
            .ShouldBe(["/api/a/old", "/api/a/one", "/api/a/two"]);
    }

    [Fact]
    public async Task TenantCaller_ListFilteredByAnotherTenantsUser_IsEmpty()
    {
        // 按用户 id 点名别租户的用户：拿到的是空，不是对方的访问记录。
        var result = await Service(TenantA).GetAccessLogsAsync(new AccessLogQueryDto { UserId = UserOfB });

        Paths(result).ShouldBeEmpty();
    }

    [Fact]
    public async Task HostCaller_List_SeesEveryTenant()
    {
        Paths(await Service(null).GetAccessLogsAsync(new AccessLogQueryDto()))
            .ShouldBe(["/api/a/old", "/api/a/one", "/api/a/two", "/api/b/old", "/api/b/one", "/api/host/one"]);
    }

    [Fact]
    public async Task TenantCaller_GetLogOfAnotherTenant_Is404_NotRevealingExistence()
    {
        var foreign = await DbContext.AccessLogs.AsNoTracking().SingleAsync(l => l.Path == "/api/b/one");
        var own = await DbContext.AccessLogs.AsNoTracking().SingleAsync(l => l.Path == "/api/a/one");

        (await Service(TenantA).GetAccessLogByIdAsync(foreign.Id)).Code.ShouldBe(404);
        (await Service(TenantA).GetAccessLogByIdAsync(own.Id)).Succeeded.ShouldBeTrue();
        (await Service(null).GetAccessLogByIdAsync(foreign.Id)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task TenantCaller_StatisticsTrendAndTopEndpoints_AreScoped()
    {
        var service = Service(TenantA);
        var since = DateTime.UtcNow.AddDays(-3);

        var stats = await service.GetAccessLogStatisticsAsync(since);
        stats.Data!.TotalRequests.ShouldBe(2);
        stats.Data.UniqueUsers.ShouldBe(1);
        stats.Data.ErrorRequests.ShouldBe(1);

        var trend = await service.GetAccessLogTrendAsync(TrendInterval.Daily, since, DateTime.UtcNow.AddDays(1));
        trend.Data!.DataPoints.Sum(p => p.TotalRequests).ShouldBe(2);
        trend.Data.DataPoints.Max(p => p.UniqueUsers).ShouldBe(1);

        var top = await service.GetTopEndpointsAsync(since);
        top.Data!.Select(e => e.Path).ShouldBe(["/api/a/one", "/api/a/two"], ignoreOrder: true);
    }

    // ---- 删除 -------------------------------------------------------------------

    [Fact]
    public async Task TenantCaller_DeleteExpired_IsRefused_AndTouchesNothing()
    {
        // 保留期清理是部署级动作，不是租户能替宿主做的事：租户内的调用者 403，一行不删。
        var result = await Service(TenantA).DeleteExpiredAccessLogsAsync(days: 1);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        (await DbContext.AccessLogs.AsNoTracking().CountAsync()).ShouldBe(6);
    }

    [Fact]
    public async Task HostCaller_DeleteExpired_StillWorks()
    {
        var result = await Service(null).DeleteExpiredAccessLogsAsync(days: 90);

        result.Succeeded.ShouldBeTrue(result.Message);
        (await DbContext.AccessLogs.AsNoTracking().CountAsync()).ShouldBe(4);
    }

    [Fact]
    public async Task TenantCaller_BatchDelete_RemovesOnlyOwnTenantRows()
    {
        var ids = await DbContext.AccessLogs.AsNoTracking()
            .Where(l => l.Path == "/api/a/one" || l.Path == "/api/b/one" || l.Path == "/api/host/one")
            .Select(l => l.Id)
            .ToListAsync();

        var result = await Service(TenantA).DeleteAccessLogsAsync(ids);

        result.Succeeded.ShouldBeTrue(result.Message);
        var remaining = await DbContext.AccessLogs.AsNoTracking().Select(l => l.Path).ToListAsync();
        remaining.ShouldNotContain("/api/a/one");
        remaining.ShouldContain("/api/b/one");
        remaining.ShouldContain("/api/host/one");
    }

    // ---- 手工上报 -------------------------------------------------------------------

    [Fact]
    public async Task ManualLog_IsStampedWithTheCallersTenant()
    {
        var sender = new CapturingSender();
        var input = new AccessLogDto { Path = "/api/manual", Method = "GET", StatusCode = 200 };

        (await Service(TenantA, sender).LogAccessAsync(input)).Succeeded.ShouldBeTrue();

        sender.Captured.ShouldHaveSingleItem().TenantId.ShouldBe(TenantA);
    }

    [Fact]
    public async Task ManualLog_CannotBePlacedUnderAnotherTenant()
    {
        // TenantId 不走线缆（JsonIgnore），但服务层也不信任一个已经带着租户的 DTO：一律以调用者的身份为准。
        var sender = new CapturingSender();
        var input = new AccessLogDto { Path = "/api/manual", Method = "GET", StatusCode = 200, TenantId = TenantB };

        (await Service(TenantA, sender).LogAccessAsync(input)).Succeeded.ShouldBeTrue();
        (await Service(null, sender).LogAccessAsync(input)).Succeeded.ShouldBeTrue();

        sender.Captured.Select(l => l.TenantId).ShouldBe([TenantA, null]);
        input.TenantId.ShouldBe(TenantB, "the caller's DTO must not be mutated");
    }
}

/// <summary>
/// 多租户关闭（默认）时 <c>AccessLog.TenantId</c> 被 Ignore：读取与删除面一行租户谓词都不能加，
/// 否则单租户部署里每一次访问日志查询都会在查询翻译时 500。
/// </summary>
public class AccessLogSingleTenantTests : IntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
    }

    public AccessLogSingleTenantTests()
    {
        DbContext.AccessLogs.AddRange(
            new AccessLog { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Path = "/api/one", Method = "GET", StatusCode = 200, ResponseTime = 5, CreationTime = DateTime.UtcNow.AddHours(-1) },
            new AccessLog { Id = Guid.NewGuid(), Path = "/api/old", Method = "GET", StatusCode = 404, ResponseTime = 5, CreationTime = DateTime.UtcNow.AddDays(-400) });
        DbContext.SaveChangesAsync().GetAwaiter().GetResult();
        DbContext.ChangeTracker.Clear();
    }

    private AccessLogService Service()
    {
        // 一个钉在租户上的调用者：多租户关闭时它的租户必须被无视，而不是变成一条引用不存在之列的谓词。
        var tenant = new Mock<ICurrentTenant>();
        tenant.Setup(t => t.Id).Returns(Guid.NewGuid());
        return new AccessLogService(
            ServiceProvider,
            new EFCoreRepository<SystemTestDbContext, AccessLog, Guid>(DbContext, serviceProvider: ServiceProvider),
            new Mock<IAccessLogSender>().Object,
            accessLogOptions: null,
            currentTenant: tenant.Object,
            multiTenancyOptions: Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions { Enabled = false }));
    }

    [Fact]
    public async Task EveryReadAndDelete_WorksWithoutATenantColumn()
    {
        var service = Service();
        var id = await DbContext.AccessLogs.AsNoTracking().Where(l => l.Path == "/api/one").Select(l => l.Id).SingleAsync();

        var list = await service.GetAccessLogsAsync(new AccessLogQueryDto());
        list.Succeeded.ShouldBeTrue(list.Message);
        list.Data!.TotalCount.ShouldBe(2);

        (await service.GetAccessLogByIdAsync(id)).Succeeded.ShouldBeTrue();
        (await service.GetAccessLogStatisticsAsync()).Data!.TotalRequests.ShouldBe(2);
        (await service.GetAccessLogTrendAsync(TrendInterval.Daily, DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(1))).Succeeded.ShouldBeTrue();
        (await service.GetTopEndpointsAsync()).Data!.Count.ShouldBe(2);

        (await service.DeleteExpiredAccessLogsAsync(90)).Succeeded.ShouldBeTrue();
        (await service.DeleteAccessLogsAsync([id])).Succeeded.ShouldBeTrue();
        (await DbContext.AccessLogs.AsNoTracking().CountAsync()).ShouldBe(0);
    }
}
