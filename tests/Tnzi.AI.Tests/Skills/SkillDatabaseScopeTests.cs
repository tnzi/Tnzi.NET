using Tnzi.Security.Authorization;
using Microsoft.Data.Sqlite;

namespace Tnzi.AI.Tests.Skills;

/// <summary>
/// 数据库技能的三处取舍，用真实 SQLite 内存库：
/// <list type="bullet">
/// <item>按 slug 查找认作用域前缀与作用域优先级，与列表合并同一口径（Tenant 胜过 User）。</item>
/// <item>数据库技能写入后只丢掉本作用域合并过的结果，不再逼文件技能存储整个重读。</item>
/// <item>按分类列技能与管理列表同一套可见性与投影；写入的分类必须存在且对调用者可见。</item>
/// </list>
/// </summary>
public class SkillDatabaseScopeTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string _tempDir;
    private readonly Guid _userId = Guid.NewGuid();

    public SkillDatabaseScopeTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));

        _tempDir = Path.Combine(Path.GetTempPath(), $"tnzi-skill-db-scope-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        using var ctx = CreateContext();
        ctx.Database.EnsureCreated();
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
    // GetBySlugAsync: scope prefix and scope precedence for database rows
    // =====================================================================

    [Theory]
    [InlineData("tenant:triage", SkillScope.Tenant)]
    [InlineData("user:triage", SkillScope.User)]
    [InlineData("triage", SkillScope.Tenant)]
    public async Task GetBySlugAsync_DatabaseRowsSharingASlug_HonourThePrefixAndTheListPrecedence(string slug, SkillScope expected)
    {
        // The personal row goes in first, so an unordered pick would find it first.
        await SeedAsync(
            new SkillEntity { Slug = "triage", Name = "Mine", Content = "x", Scope = SkillScope.User, OwnerUserId = _userId, Enabled = true },
            new SkillEntity { Slug = "triage", Name = "Shared", Content = "x", Scope = SkillScope.Tenant, Enabled = true });

        await using var ctx = CreateContext();
        var registry = CreateRegistry(ctx, out _);

        var found = await registry.GetBySlugAsync(slug);

        found.ShouldNotBeNull();
        found.Scope.ShouldBe(expected);
    }

    [Theory]
    [InlineData("tenant:triage", SkillScope.Tenant)]
    [InlineData("user:triage", SkillScope.User)]
    [InlineData("triage", SkillScope.Tenant)]
    public async Task GetBySlugAsync_AfterTheListWasLoaded_AgreesWithTheList(string slug, SkillScope expected)
    {
        await SeedAsync(
            new SkillEntity { Slug = "triage", Name = "Mine", Content = "x", Scope = SkillScope.User, OwnerUserId = _userId, Enabled = true },
            new SkillEntity { Slug = "triage", Name = "Shared", Content = "x", Scope = SkillScope.Tenant, Enabled = true });

        await using var ctx = CreateContext();
        var registry = CreateRegistry(ctx, out _);

        var listed = await registry.GetAvailableSkillsAsync();
        listed.Single(s => s.Slug == "triage").Scope.ShouldBe(SkillScope.Tenant);

        var found = await registry.GetBySlugAsync(slug);

        found.ShouldNotBeNull();
        found.Scope.ShouldBe(expected);
    }

    [Fact]
    public async Task GetBySlugAsync_UserPrefixWithoutAPersonalRow_ReturnsNull()
    {
        await SeedAsync(new SkillEntity { Slug = "triage", Name = "Shared", Content = "x", Scope = SkillScope.Tenant, Enabled = true });

        await using var ctx = CreateContext();
        var registry = CreateRegistry(ctx, out _);

        (await registry.GetBySlugAsync("user:triage")).ShouldBeNull();
    }

    // =====================================================================
    // InvalidateDatabaseSkills: database writes do not reread file skills
    // =====================================================================

    [Fact]
    public async Task InvalidateDatabaseSkills_ShowsTheNewDatabaseRow_WithoutRereadingFileSkills()
    {
        WriteSkillFile("on-disk", "---\nname: On Disk\ndescription: Loaded from a file.\n---");

        await using var ctx = CreateContext();
        var registry = CreateRegistry(ctx, out _);

        (await registry.GetAvailableSkillsAsync()).Select(s => s.Slug).ShouldBe(["on-disk"]);

        // A file appears on disk and a row lands in the table in the same request.
        WriteSkillFile("added-later", "---\nname: Added Later\ndescription: Not reread.\n---");
        ctx.Set<SkillEntity>().Add(new SkillEntity { Slug = "db-row", Name = "Db Row", Content = "x", Scope = SkillScope.Tenant, Enabled = true });
        await ctx.SaveChangesAsync();

        registry.InvalidateDatabaseSkills();

        var slugs = (await registry.GetAvailableSkillsAsync()).Select(s => s.Slug).ToList();
        slugs.ShouldContain("db-row");
        slugs.ShouldNotContain("added-later");
    }

    [Fact]
    public async Task InvalidateCache_StillRereadsFileSkills()
    {
        WriteSkillFile("on-disk", "---\nname: On Disk\ndescription: Loaded from a file.\n---");

        await using var ctx = CreateContext();
        var registry = CreateRegistry(ctx, out _);
        await registry.GetAvailableSkillsAsync();

        WriteSkillFile("added-later", "---\nname: Added Later\ndescription: Reread.\n---");
        registry.InvalidateCache();

        (await registry.GetAvailableSkillsAsync()).Select(s => s.Slug).ShouldContain("added-later");
    }

    // =====================================================================
    // Skills by category: visibility, projection, and the category written
    // =====================================================================

    [Fact]
    public async Task GetSkillsByCategoryAsync_HidesRowsOfAnotherTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var category = new SkillCategory { Name = "Support", Slug = "support", TenantId = tenantA };
        await SeedCategoryAsync(category);
        await SeedAsync(
            new SkillEntity { Slug = "a-skill", Name = "A", Content = "x", Scope = SkillScope.Tenant, TenantId = tenantA, CategoryId = category.Id, Enabled = true },
            // A tenant-B user pointed their personal skill at tenant A's category.
            new SkillEntity { Slug = "b-skill", Name = "B", Content = "x", Scope = SkillScope.User, TenantId = tenantB, OwnerUserId = Guid.NewGuid(), CategoryId = category.Id, Enabled = true });

        await using var ctx = CreateContext();
        var service = CreateCategoryService(ctx, tenantA);

        var result = await service.GetSkillsByCategoryAsync(category.Id);

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Select(s => s.Slug).ShouldBe(["a-skill"]);
    }

    [Fact]
    public async Task GetSkillsByCategoryAsync_ReportsDatabaseRowsAsDatabaseSkillsWithTheirTags()
    {
        var category = new SkillCategory { Name = "Support", Slug = "support" };
        await SeedCategoryAsync(category);
        var row = new SkillEntity
        {
            Slug = "tagged", Name = "Tagged", Content = "x", Scope = SkillScope.Tenant, CategoryId = category.Id, Enabled = true,
            TagsJson = "[\"billing\",\"refunds\"]"
        };
        await SeedAsync(row);

        await using var ctx = CreateContext();
        var service = CreateCategoryService(ctx, tenantId: null);

        var result = await service.GetSkillsByCategoryAsync(category.Id);

        result.Succeeded.ShouldBeTrue(result.Message);
        var dto = result.Data!.Single();
        dto.Id.ShouldBe(row.Id);
        dto.Source.ShouldBe(SkillSource.Database);
        dto.Tags.ShouldBe(["billing", "refunds"]);
        dto.CategoryId.ShouldBe(category.Id);
    }

    [Fact]
    public async Task CreateAsync_WithACategoryThatDoesNotExist_Returns404AndWritesNothing()
    {
        await using var ctx = CreateContext();
        var service = CreateSkillService(ctx);

        var result = await service.CreateAsync(new CreateSkillDto
        {
            Slug = "orphan", Name = "Orphan", Content = "x", Scope = SkillScope.User, CategoryId = Guid.NewGuid()
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
        (await ctx.Set<SkillEntity>().AnyAsync(e => e.Slug == "orphan")).ShouldBeFalse();
    }

    [Fact]
    public async Task CreateAsync_WithAnExistingCategory_Succeeds()
    {
        var category = new SkillCategory { Name = "Support", Slug = "support" };
        await SeedCategoryAsync(category);

        await using var ctx = CreateContext();
        var service = CreateSkillService(ctx);

        var result = await service.CreateAsync(new CreateSkillDto
        {
            Slug = "filed", Name = "Filed", Content = "x", Scope = SkillScope.User, CategoryId = category.Id
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.CategoryId.ShouldBe(category.Id);
    }

    [Fact]
    public async Task UpdateAsync_WithACategoryThatDoesNotExist_Returns404AndKeepsTheOldCategory()
    {
        var category = new SkillCategory { Name = "Support", Slug = "support" };
        await SeedCategoryAsync(category);
        var row = new SkillEntity { Slug = "mine", Name = "Mine", Content = "x", Scope = SkillScope.User, OwnerUserId = _userId, CategoryId = category.Id, Enabled = true };
        await SeedAsync(row);

        await using var ctx = CreateContext();
        var service = CreateSkillService(ctx);

        var result = await service.UpdateAsync(row.Id, new UpdateSkillDto { CategoryId = Guid.NewGuid() });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
        await using var verify = CreateContext();
        (await verify.Set<SkillEntity>().SingleAsync(e => e.Id == row.Id)).CategoryId.ShouldBe(category.Id);
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private async Task SeedAsync(params SkillEntity[] rows)
    {
        await using var ctx = CreateContext();
        foreach (var row in rows)
        {
            ctx.Set<SkillEntity>().Add(row);
            await ctx.SaveChangesAsync();
        }
    }

    private async Task SeedCategoryAsync(SkillCategory category)
    {
        await using var ctx = CreateContext();
        ctx.Set<SkillCategory>().Add(category);
        await ctx.SaveChangesAsync();
    }

    private void WriteSkillFile(string dirName, string content)
    {
        var dir = Path.Combine(_tempDir, dirName);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), content);
    }

    private SkillTenantDbContext CreateContext()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(m => m.Id).Returns(_userId);
        currentUser.Setup(m => m.IsAuthenticated).Returns(true);
        currentUser.Setup(m => m.TenantId).Returns((Guid?)null);

        var options = new DbContextOptionsBuilder<SkillTenantDbContext>().UseSqlite(_connection).Options;
        return new SkillTenantDbContext(options, currentUser.Object);
    }

    private FileSystemSkillStore CreateFileStore()
    {
        var options = new StaticOptionsMonitor<AIOptions>(new AIOptions
        {
            ContextProviders = new ContextProvidersOptions
            {
                Skills = new SkillsOptions { Paths = [_tempDir], AllowList = [], DenyList = [], RequireChecksEnabled = false, LoadBuiltIn = false }
            }
        });
        return new FileSystemSkillStore(Mock.Of<ILogger<FileSystemSkillStore>>(), options);
    }

    private ICurrentUser CurrentUser()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(m => m.Id).Returns(_userId);
        currentUser.Setup(m => m.IsAuthenticated).Returns(true);
        return currentUser.Object;
    }

    private SkillRegistry CreateRegistry(SkillTenantDbContext ctx, out DatabaseSkillStore dbStore)
    {
        var repo = new EFCoreRepository<SkillTenantDbContext, SkillEntity, Guid>(ctx);
        dbStore = new DatabaseSkillStore(repo, NullLogger<DatabaseSkillStore>.Instance, CurrentUser());
        return new SkillRegistry(
            CreateFileStore(),
            new SkillSearchService(NullLogger<SkillSearchService>.Instance),
            dbStore,
            CurrentUser());
    }

    private SkillCategoryService CreateCategoryService(SkillTenantDbContext ctx, Guid? tenantId)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return new SkillCategoryService(
            services.BuildServiceProvider(),
            new EFCoreRepository<SkillTenantDbContext, SkillCategory, Guid>(ctx),
            new EFCoreRepository<SkillTenantDbContext, SkillEntity, Guid>(ctx),
            tenantId.HasValue ? new StubCurrentTenant(tenantId.Value) : null);
    }

    private SkillService CreateSkillService(SkillTenantDbContext ctx)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => CurrentUser());
        services.AddScoped(_ => Mock.Of<IPermissionChecker>());
        var sp = services.BuildServiceProvider();

        return new SkillService(
            sp,
            new EFCoreRepository<SkillTenantDbContext, SkillEntity, Guid>(ctx),
            new EFCoreRepository<SkillTenantDbContext, SkillCategory, Guid>(ctx),
            Mock.Of<ISkillRegistry>(),
            Mock.Of<ISkillTemplateEngine>(),
            CreateFileStore());
    }

    private sealed class StubCurrentTenant(Guid? tenantId) : ICurrentTenant
    {
        public Guid? Id { get; } = tenantId;
        public string? Name => null;
        public bool IsAvailable => Id.HasValue;
        public IDisposable Change(Guid? tenantId, string? tenantName = null) => new NoOp();
        private sealed class NoOp : IDisposable { public void Dispose() { } }
    }
}
