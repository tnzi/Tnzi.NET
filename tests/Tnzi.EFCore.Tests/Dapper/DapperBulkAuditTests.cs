using Tnzi.EFCore.Dapper;

namespace Tnzi.EFCore.Tests.Dapper;

/// <summary>
/// Dapper 批量写入绕过变更跟踪器，审计列与租户列必须仍按 <c>SaveChanges</c> 同一套规则填充。
/// </summary>
/// <remarks>
/// <para>
/// <c>BulkInsertAsync</c> 解锁 Guid 主键后，其余列仍从 CLR 对象逐字送库：
/// <c>CreationTime</c> 0001-01-01、<c>CreatorId</c> / <c>TenantId</c> 为 null。多租户过滤器是严格等值，
/// 于是一批 200 落库的行对写入它们的租户不可见；SQL Server <c>datetime</c> 列上 0001-01-01 直接越界。
/// 此前 Guid 键整批 NOT NULL 违例是响亮的失败，解锁主键把它换成了无声的。
/// </para>
/// <para>
/// 真实 SQLite + 经 DI 解析的 <see cref="TestDbContext"/>（与运行期同一条协作者解析路径），不 Mock 被测对象。
/// </para>
/// </remarks>
public class DapperBulkAuditTests : EFCoreTestBase
{
    private DapperService CreateService() => new(DbContext, new SqliteTestProvider());

    [Fact]
    public async Task BulkInsertAsync_CreationAuditedEntity_FillsCreationTimeAndCreator()
    {
        var userId = Guid.NewGuid();
        ((MockCurrentUser)CurrentUser).SetUser(userId, "bulk-writer");
        var before = DateTime.UtcNow.AddSeconds(-1);
        var user = new TestUser { UserName = "audited", Email = "audited@t.test" };

        await CreateService().BulkInsertAsync([user]);

        var reloaded = await DbContext.Users.AsNoTracking().IgnoreQueryFilters().SingleAsync(u => u.Id == user.Id);
        Assert.True(reloaded.CreationTime >= before, $"CreationTime was {reloaded.CreationTime:O}");
        Assert.Equal(userId, reloaded.CreatorId);
    }

    [Fact]
    public async Task BulkInsertAsync_ConcurrencyStampedEntity_IssuesAStamp()
    {
        var document = new TestAuditedDocument { Title = "stamped" };

        await CreateService().BulkInsertAsync([document]);

        var reloaded = await DbContext.AuditedDocuments.AsNoTracking().SingleAsync(d => d.Id == document.Id);
        Assert.False(string.IsNullOrEmpty(reloaded.ConcurrencyStamp));
        Assert.Equal(document.ConcurrencyStamp, reloaded.ConcurrencyStamp);
    }

    /// <summary>与 SaveChanges 同一规则：调用方已经赋的审计值不被覆盖。</summary>
    [Fact]
    public async Task BulkInsertAsync_PreassignedAuditValues_AreKept()
    {
        ((MockCurrentUser)CurrentUser).SetUser(Guid.NewGuid(), "someone-else");
        var presetTime = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var presetCreator = Guid.NewGuid();
        var user = new TestUser
        {
            UserName = "preset",
            Email = "preset@t.test",
            CreationTime = presetTime,
            CreatorId = presetCreator,
        };

        await CreateService().BulkInsertAsync([user]);

        var reloaded = await DbContext.Users.AsNoTracking().IgnoreQueryFilters().SingleAsync(u => u.Id == user.Id);
        Assert.Equal(presetTime, reloaded.CreationTime);
        Assert.Equal(presetCreator, reloaded.CreatorId);
    }

    /// <summary>
    /// 评审指出的形态：多租户开启、在租户作用域内批量插入，行必须对写入它的租户可见。
    /// 上下文按 <c>CombinedQueryFilterTests</c> 的方式手工构造（无应用容器），协作者只能来自上下文自身。
    /// 多租户关闭时 <c>TenantId</c> 被 Ignore 出模型，所以租户列只能在这个夹具上断言。
    /// </summary>
    [Fact]
    public async Task BulkInsertAsync_MultiTenancyEnabled_RowsAreVisibleToTheWritingTenant()
    {
        using var fixture = new MultiTenantFixture();
        var tenantId = Guid.NewGuid();
        using var context = fixture.CreateContext(tenantId);
        var users = new[]
        {
            new TestUser { UserName = "mt-1", Email = "mt1@t.test" },
            new TestUser { UserName = "mt-2", Email = "mt2@t.test" },
        };

        await new DapperService(context, new SqliteTestProvider()).BulkInsertAsync(users);

        var visible = await context.Users.Select(u => u.UserName).OrderBy(n => n).ToListAsync();
        Assert.Equal(["mt-1", "mt-2"], visible);
        var stored = await context.Users.IgnoreQueryFilters().Select(u => u.TenantId).Distinct().ToListAsync();
        Assert.Equal([tenantId], stored);
    }

    /// <summary>调用方显式写的租户不被当前租户覆盖（与 SaveChanges 同一规则）。</summary>
    [Fact]
    public async Task BulkInsertAsync_MultiTenancyEnabled_PreassignedTenantIsKept()
    {
        using var fixture = new MultiTenantFixture();
        var writingTenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        using var context = fixture.CreateContext(writingTenant);
        var user = new TestUser { UserName = "other-tenant-row", Email = "o@t.test", TenantId = otherTenant };

        await new DapperService(context, new SqliteTestProvider()).BulkInsertAsync([user]);

        var stored = await context.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == user.Id);
        Assert.Equal(otherTenant, stored.TenantId);
        Assert.Empty(await context.Users.ToListAsync());
    }

    [Fact]
    public async Task BulkUpdateAsync_ModificationAuditedEntity_StampsModifierAndTime()
    {
        var user = new TestUser { UserName = "to-update", Email = "u@t.test" };
        DbContext.Users.Add(user);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        var modifierId = Guid.NewGuid();
        ((MockCurrentUser)CurrentUser).SetUser(modifierId, "modifier");
        var before = DateTime.UtcNow.AddSeconds(-1);
        var detached = new TestUser
        {
            Id = user.Id,
            UserName = "updated",
            Email = user.Email,
            CreationTime = user.CreationTime,
            TenantId = user.TenantId,
        };

        await CreateService().BulkUpdateAsync([detached]);

        var reloaded = await DbContext.Users.AsNoTracking().IgnoreQueryFilters().SingleAsync(u => u.Id == user.Id);
        Assert.Equal("updated", reloaded.UserName);
        Assert.Equal(modifierId, reloaded.LastModifierId);
        Assert.NotNull(reloaded.LastModificationTime);
        Assert.True(reloaded.LastModificationTime >= before, $"LastModificationTime was {reloaded.LastModificationTime:O}");
    }

    [Fact]
    public async Task BulkUpdateAsync_ConcurrencyStampedEntity_IssuesAFreshStamp()
    {
        var document = new TestAuditedDocument { Title = "v1" };
        DbContext.AuditedDocuments.Add(document);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        var originalStamp = document.ConcurrencyStamp;
        var detached = new TestAuditedDocument
        {
            Id = document.Id,
            Title = "v2",
            CreationTime = document.CreationTime,
            ConcurrencyStamp = originalStamp,
        };

        await CreateService().BulkUpdateAsync([detached]);

        var reloaded = await DbContext.AuditedDocuments.AsNoTracking().SingleAsync(d => d.Id == document.Id);
        Assert.Equal("v2", reloaded.Title);
        Assert.NotEqual(originalStamp, reloaded.ConcurrencyStamp);
        Assert.Equal(detached.ConcurrencyStamp, reloaded.ConcurrencyStamp);
    }

    /// <summary>多租户开启的独立 SQLite 库；上下文手工构造，协作者只能来自上下文自身。</summary>
    private sealed class MultiTenantFixture : IDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private readonly DbContextOptions<TestDbContext> _options;

        public MultiTenantFixture()
        {
            _connection.Open();
            _options = new DbContextOptionsBuilder<TestDbContext>()
                .UseSqlite(_connection)
                .UseTnziMultiTenancy(true)
                .Options;
            using var seed = CreateContext(Guid.NewGuid());
            seed.Database.EnsureCreated();
        }

        public TestDbContext CreateContext(Guid tenantId)
        {
            var tenant = new MockCurrentTenant();
            tenant.SetTenant(tenantId, "tenant");
            return new TestDbContext(_options, new MockCurrentUser(), tenant);
        }

        public void Dispose() => _connection.Dispose();
    }
}
