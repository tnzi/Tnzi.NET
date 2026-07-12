using Microsoft.Data.Sqlite;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Skills;

/// <summary>
/// P0 多租户隔离门禁 — 证明 <see cref="SkillService"/> 的 admin 路径在多租户上下文下
/// 强制服务层可见性过滤（<c>ApplyTenantVisibility</c>）。
/// <para>
/// <see cref="SkillEntity"/> 是 <see cref="IScopedResource"/>（有意不实现 <see cref="IMultiTenant"/>，
/// 以便 System 行 TenantId=null 对所有租户可见），因此没有 EF 全局多租户查询过滤器把别的租户行挡掉，
/// 服务层 MUST 显式过滤。本测试证明：租户 A <b>看不到 / 改不了 / 删不了</b> 租户 B 的 Tenant-scope 技能，
/// 且批量删除不会跨租户。
/// </para>
/// 测试用真实 SQLite 内存库，覆盖到 EF 原生 <c>ExecuteDeleteAsync</c>（MockQueryable 无法模拟）。
/// </summary>
public class SkillServiceTenantIsolationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly string _tempDir;

    private Guid _systemSkillId;
    private Guid _tenantASkillId;
    private Guid _tenantBSkillId;

    public SkillServiceTenantIsolationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));

        _tempDir = Path.Combine(Path.GetTempPath(), $"tnzi-skill-tenant-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        using var seed = CreateContext();
        seed.Database.EnsureCreated();
        SeedData(seed);
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
        GC.SuppressFinalize(this);
    }

    // =====================================================================
    // GetPagedAsync — tenant A sees System ∪ own, never tenant B
    // =====================================================================

    [Fact]
    public async Task GetPagedAsync_TenantA_SeesSystemAndOwn_NeverTenantB()
    {
        await using var ctx = CreateContext();
        var service = CreateService(ctx, _tenantA);

        var result = await service.GetPagedAsync(new SkillQueryDto { PageIndex = 1, PageSize = 50 });

        result.Succeeded.ShouldBeTrue(result.Message);
        var slugs = result.Data!.Items.Select(s => s.Slug).ToList();
        slugs.ShouldContain("system-skill");
        slugs.ShouldContain("tenant-a-skill");
        slugs.ShouldNotContain("tenant-b-skill");
    }

    [Fact]
    public async Task GetPagedAsync_SingleTenant_NoFilter_SeesAll()
    {
        await using var ctx = CreateContext();
        var service = CreateService(ctx, currentTenantId: null); // single-tenant host

        var result = await service.GetPagedAsync(new SkillQueryDto { PageIndex = 1, PageSize = 50 });

        result.Succeeded.ShouldBeTrue(result.Message);
        var slugs = result.Data!.Items.Select(s => s.Slug).ToList();
        slugs.ShouldContain("system-skill");
        slugs.ShouldContain("tenant-a-skill");
        slugs.ShouldContain("tenant-b-skill");
    }

    // =====================================================================
    // UpdateAsync — tenant A cannot update tenant B's row (404) or System (403)
    // =====================================================================

    [Fact]
    public async Task UpdateAsync_TenantA_CannotUpdateTenantBRow_Returns404()
    {
        await using var ctx = CreateContext();
        var service = CreateService(ctx, _tenantA);

        var result = await service.UpdateAsync(_tenantBSkillId, new UpdateSkillDto { Name = "Hacked" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task UpdateAsync_TenantA_CannotUpdateSystemRow_Returns403()
    {
        await using var ctx = CreateContext();
        var service = CreateService(ctx, _tenantA);

        var result = await service.UpdateAsync(_systemSkillId, new UpdateSkillDto { Name = "Hacked" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
    }

    [Fact]
    public async Task UpdateAsync_TenantA_CanUpdateOwnRow()
    {
        await using var ctx = CreateContext();
        var service = CreateService(ctx, _tenantA);

        var result = await service.UpdateAsync(_tenantASkillId, new UpdateSkillDto { Name = "Renamed" });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Name.ShouldBe("Renamed");
    }

    // =====================================================================
    // DeleteAsync — tenant A cannot delete tenant B's row (404) or System (403)
    // =====================================================================

    [Fact]
    public async Task DeleteAsync_TenantA_CannotDeleteTenantBRow_Returns404()
    {
        await using var ctx = CreateContext();
        var service = CreateService(ctx, _tenantA);

        var result = await service.DeleteAsync(_tenantBSkillId);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);

        // Tenant B's row must still exist.
        (await ctx.Set<SkillEntity>().AnyAsync(e => e.Id == _tenantBSkillId)).ShouldBeTrue();
    }

    [Fact]
    public async Task DeleteAsync_TenantA_CannotDeleteSystemRow_Returns403()
    {
        await using var ctx = CreateContext();
        var service = CreateService(ctx, _tenantA);

        var result = await service.DeleteAsync(_systemSkillId);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
    }

    // =====================================================================
    // BatchDeleteAsync — never crosses tenant boundary
    // =====================================================================

    [Fact]
    public async Task BatchDeleteAsync_TenantA_OnlyDeletesOwnRows_SkipsTenantBAndSystem()
    {
        await using var ctx = CreateContext();
        var service = CreateService(ctx, _tenantA);

        // Attempt to batch-delete ALL three rows; only tenant A's row may be deleted.
        var result = await service.BatchDeleteAsync([_systemSkillId, _tenantASkillId, _tenantBSkillId]);

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data.ShouldBe(1); // only tenant A's row

        await using var verify = CreateContext();
        (await verify.Set<SkillEntity>().AnyAsync(e => e.Id == _tenantASkillId)).ShouldBeFalse("tenant A's row should be deleted");
        (await verify.Set<SkillEntity>().AnyAsync(e => e.Id == _tenantBSkillId)).ShouldBeTrue("tenant B's row must survive");
        (await verify.Set<SkillEntity>().AnyAsync(e => e.Id == _systemSkillId)).ShouldBeTrue("System row must survive");
    }

    [Fact]
    public async Task BatchSetEnabledAsync_TenantA_OnlyTogglesOwnRows()
    {
        await using var ctx = CreateContext();
        var service = CreateService(ctx, _tenantA);

        var result = await service.BatchSetEnabledAsync([_systemSkillId, _tenantASkillId, _tenantBSkillId], enabled: false);

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data.ShouldBe(1); // only tenant A's row toggled

        await using var verify = CreateContext();
        (await verify.Set<SkillEntity>().FirstAsync(e => e.Id == _tenantASkillId)).Enabled.ShouldBeFalse();
        (await verify.Set<SkillEntity>().FirstAsync(e => e.Id == _tenantBSkillId)).Enabled.ShouldBeTrue();
        (await verify.Set<SkillEntity>().FirstAsync(e => e.Id == _systemSkillId)).Enabled.ShouldBeTrue();
    }

    // =====================================================================
    // Seed + factories
    // =====================================================================

    private void SeedData(SkillTenantDbContext ctx)
    {
        var system = new SkillEntity { Slug = "system-skill", Name = "System Skill", Content = "x", Scope = SkillScope.System, TenantId = null, Enabled = true };
        var tenantA = new SkillEntity { Slug = "tenant-a-skill", Name = "Tenant A Skill", Content = "x", Scope = SkillScope.Tenant, TenantId = _tenantA, Enabled = true };
        var tenantB = new SkillEntity { Slug = "tenant-b-skill", Name = "Tenant B Skill", Content = "x", Scope = SkillScope.Tenant, TenantId = _tenantB, Enabled = true };

        ctx.Set<SkillEntity>().AddRange(system, tenantA, tenantB);
        ctx.SaveChangesAsync().GetAwaiter().GetResult();

        _systemSkillId = system.Id;
        _tenantASkillId = tenantA.Id;
        _tenantBSkillId = tenantB.Id;
    }

    private SkillTenantDbContext CreateContext()
    {
        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(m => m.Id).Returns(Guid.Empty);
        currentUserMock.Setup(m => m.IsAuthenticated).Returns(false);
        currentUserMock.Setup(m => m.TenantId).Returns((Guid?)null);

        var options = new DbContextOptionsBuilder<SkillTenantDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new SkillTenantDbContext(options, currentUserMock.Object);
    }

    private SkillService CreateService(SkillTenantDbContext ctx, Guid? currentTenantId)
    {
        var repo = new EFCoreRepository<SkillTenantDbContext, SkillEntity, Guid>(ctx);

        var registry = new Mock<ISkillRegistry>();
        registry.Setup(r => r.InvalidateCache());
        var templateEngine = new Mock<ISkillTemplateEngine>();

        var aiOptions = new StaticOptionsMonitor<AIOptions>(new AIOptions
        {
            ContextProviders = new ContextProvidersOptions
            {
                Skills = new SkillsOptions { Paths = [_tempDir], AllowList = [], DenyList = [], RequireChecksEnabled = false }
            }
        });
        var fileStore = new FileSystemSkillStore(Mock.Of<ILogger<FileSystemSkillStore>>(), aiOptions);

        var sp = new ServiceCollection().AddLogging().BuildServiceProvider();

        return new SkillService(
            sp,
            repo,
            registry.Object,
            templateEngine.Object,
            fileStore,
            requirementsValidator: null,
            currentTenant: currentTenantId.HasValue ? new StubCurrentTenant(currentTenantId.Value) : null);
    }

    private sealed class StubCurrentTenant : ICurrentTenant
    {
        public StubCurrentTenant(Guid? tenantId) { Id = tenantId; }
        public Guid? Id { get; }
        public string? Name => null;
        public bool IsAvailable => Id.HasValue;
        public IDisposable Change(Guid? tenantId, string? tenantName = null) => new NoOp();
        private sealed class NoOp : IDisposable { public void Dispose() { } }
    }
}

/// <summary>
/// 测试专用 DbContext — 注册 SkillEntity(IScopedResource) + SkillCategory(FK 目标)。
/// SkillEntity 不是 IMultiTenant，无全局多租户过滤器；可见性全部交由服务层。
/// </summary>
internal sealed class SkillTenantDbContext : TnziDbContext<SkillTenantDbContext>
{
    public SkillTenantDbContext(DbContextOptions<SkillTenantDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new SkillCategoryConfiguration());
        modelBuilder.ApplyConfiguration(new SkillEntityConfiguration());

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}
