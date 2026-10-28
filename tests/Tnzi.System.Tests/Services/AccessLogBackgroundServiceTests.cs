using System.Threading.Channels;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.Services;
using Tnzi.System.Tests.Integration;

namespace Tnzi.System.Tests.Services;

/// <summary>
/// <see cref="AccessLogBackgroundService"/>：一行坏数据不能连带抹掉同一批里其他人的访问日志。
/// </summary>
/// <remarks>
/// <para>
/// 访问日志按批（默认 100 条）用一条 <c>InsertMany</c> 落库，基类对失败的一批是「记日志后整批丢弃」。
/// 两条规则叠在一起，任何一行被数据库拒绝都会带走同一批里所有人的记录。而 User-Agent 解析出的版本号、设备型号
/// 长度不受约束、直接取自请求头：一个匿名客户端反复发一个版本号超长的 UA，就能让统计、趋势与 Top 端点按需少算。
/// </para>
/// <para>
/// ★ SQLite 不检查 VARCHAR 长度，所以「超列宽被拒」在这里复现不出来。两件事分开断言：
/// 落库前每个有列宽的字段都已收进列宽（直接对模型里的 MaxLength 比）；数据库拒绝一行时只丢那一行
/// （用一个拦截器模拟数据库拒绝含某一行的语句）。
/// </para>
/// </remarks>
public class AccessLogBackgroundServiceTests : IntegrationTestBase
{
    /// <summary>真实的 User-Agent 解析器会把这个版本号原样交出来：33 个字符，列宽 30。</summary>
    private const string CraftedUserAgent = "Mozilla/5.0 (Linux; Android 10) Chrome/1.1.1.1.1.1.1.1.1.1.1.1.1.1.1.1.1";

    private const string PoisonPath = "/api/poison";

    protected override void ConfigureServices(IServiceCollection services)
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
        services.AddScoped<Tnzi.Domain.Repositories.IRepository<AccessLog, Guid>, EFCoreRepository<SystemTestDbContext, AccessLog, Guid>>();
        services.AddSingleton<IUserAgentParserService, UserAgentParserService>();
        services.AddSingleton<IIpLocatorService, VerboseIpLocator>();
    }

    protected override void ConfigureDbContextOptions(DbContextOptionsBuilder options)
        => options.AddInterceptors(new RejectPoisonRowInterceptor());

    /// <summary>归属地库返回的地名长度同样不受约束（第三方数据源）。</summary>
    private sealed class VerboseIpLocator : IIpLocatorService
    {
        public Task<IpLocationResult?> LocateAsync(string ipAddress, CancellationToken cancellationToken = default)
            => Task.FromResult<IpLocationResult?>(new IpLocationResult
            {
                IpAddress = ipAddress,
                Country = new string('C', 120),
                Province = new string('P', 120),
                City = new string('T', 120),
                District = new string('D', 120),
                Isp = new string('I', 300),
                FullAddress = new string('F', 600),
            });

        public async Task<Dictionary<string, IpLocationResult?>> LocateBatchAsync(IEnumerable<string> ipAddresses, CancellationToken cancellationToken = default)
        {
            var results = new Dictionary<string, IpLocationResult?>();
            foreach (var ip in ipAddresses)
                results[ip] = await LocateAsync(ip, cancellationToken);
            return results;
        }
    }

    /// <summary>模拟数据库拒绝一条含有某一行的 INSERT（真实库里是超列宽、约束冲突之类）。</summary>
    private sealed class RejectPoisonRowInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var poisoned = eventData.Context!.ChangeTracker.Entries<AccessLog>()
                .Any(entry => entry.State == EntityState.Added && entry.Entity.Path == PoisonPath);
            if (poisoned)
                throw new DbUpdateException("Simulated: the database rejected the INSERT statement.");

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private static AccessLogDto Entry(string path, string? userAgent = "UnitTest/1.0") => new()
    {
        UserId = Guid.NewGuid(),
        UserName = "someone",
        Path = path,
        Method = "GET",
        IpAddress = "203.0.113.7",
        UserAgent = userAgent,
        StatusCode = 200,
        ResponseTime = 12,
        CreationTime = DateTime.UtcNow,
    };

    private async Task<List<AccessLog>> SavedAsync()
        => await DbContext.AccessLogs.AsNoTracking().ToListAsync();

    [Fact]
    public async Task CraftedUserAgentAndVerboseLocation_AreFittedToEveryColumnWidth()
    {
        await new TestableService(ServiceProvider).RunBatchAsync([Entry("/api/a", CraftedUserAgent), Entry("/api/b")]);

        var saved = await SavedAsync();
        saved.Count.ShouldBe(2);

        // 对照模型本身：任何一个声明了列宽的字符串属性，落库的值都必须装得进去。
        var entityType = DbContext.Model.FindEntityType(typeof(AccessLog)).ShouldNotBeNull();
        var bounded = entityType.GetProperties()
            .Where(p => p.ClrType == typeof(string) && p.GetMaxLength() is not null)
            .ToList();
        bounded.ShouldNotBeEmpty();

        foreach (var log in saved)
        {
            foreach (var property in bounded)
            {
                var value = (string?)property.PropertyInfo!.GetValue(log);
                (value?.Length ?? 0).ShouldBeLessThanOrEqualTo(
                    property.GetMaxLength()!.Value,
                    $"{property.Name} ({value?.Length} chars) does not fit its {property.GetMaxLength()}-char column.");
            }
        }

        // 收进列宽是截断，不是丢弃：解析结果的开头仍在。
        saved.Single(l => l.Path == "/api/a").UaBrowserVersion.ShouldStartWith("1.1.1");
    }

    [Fact]
    public async Task OneRowTheDatabaseRejects_DropsOnlyThatRow()
    {
        await new TestableService(ServiceProvider).RunBatchAsync([Entry("/api/first"), Entry(PoisonPath), Entry("/api/last")]);

        var paths = (await SavedAsync()).Select(l => l.Path).OrderBy(p => p, StringComparer.Ordinal).ToList();
        paths.ShouldBe(["/api/first", "/api/last"]);
    }

    [Fact]
    public async Task QueuedIdsAreNotTrusted_SoADuplicateCannotBreakTheBatch()
    {
        // 手工上报的入口收的是完整 DTO：一个重复或已存在的 id 曾能让整批 AddRange / INSERT 失败。
        var duplicated = Guid.NewGuid();
        var first = Entry("/api/one");
        first.Id = duplicated;
        var second = Entry("/api/two");
        second.Id = duplicated;

        await new TestableService(ServiceProvider).RunBatchAsync([first, second]);

        var saved = await SavedAsync();
        saved.Count.ShouldBe(2);
        saved.Select(l => l.Id).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task TenantCarriedOnTheQueue_IsIgnoredWhenMultiTenancyIsOff()
    {
        var entry = Entry("/api/tenant");
        entry.TenantId = Guid.NewGuid();

        await new TestableService(ServiceProvider).RunBatchAsync([entry]);

        // 多租户关闭（本类默认）时这一列不存在：值被安静地忽略，而不是让落库失败。
        (await SavedAsync()).ShouldHaveSingleItem().Path.ShouldBe("/api/tenant");
    }
}

/// <summary>多租户开启时，队列上带着的租户随行落库：读取面按它收口。</summary>
public class AccessLogBackgroundServiceTenantTests : IntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
        services.Configure<Tnzi.MultiTenancy.MultiTenancyOptions>(o => o.Enabled = true);
        services.AddScoped<Tnzi.Domain.Repositories.IRepository<AccessLog, Guid>, EFCoreRepository<SystemTestDbContext, AccessLog, Guid>>();
    }

    [Fact]
    public async Task TenantCarriedOnTheQueue_IsPersisted()
    {
        var tenantId = Guid.NewGuid();
        var entry = new AccessLogDto { Path = "/api/tenant", Method = "GET", StatusCode = 200, CreationTime = DateTime.UtcNow, TenantId = tenantId };

        await new TestableService(ServiceProvider).RunBatchAsync([entry]);

        (await DbContext.AccessLogs.AsNoTracking().SingleAsync()).TenantId.ShouldBe(tenantId);
    }
}

/// <summary>把受保护的批处理入口露出来，直接喂一批进去。</summary>
internal sealed class TestableService : AccessLogBackgroundService
{
    private readonly IServiceProvider _root;

    public TestableService(IServiceProvider serviceProvider)
        : base(new StubConsumer(), serviceProvider, NullLogger<AccessLogBackgroundService>.Instance)
    {
        _root = serviceProvider;
    }

    public async Task RunBatchAsync(IReadOnlyList<AccessLogDto> batch)
    {
        using var scope = _root.CreateScope();
        await ProcessBatchAsync(batch, scope.ServiceProvider, CancellationToken.None);
    }

    private sealed class StubConsumer : IAccessLogConsumer
    {
        public ChannelReader<AccessLogDto> Reader { get; } = Channel.CreateUnbounded<AccessLogDto>().Reader;
    }
}
