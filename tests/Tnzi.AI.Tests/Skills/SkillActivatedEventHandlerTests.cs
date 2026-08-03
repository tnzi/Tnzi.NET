
namespace Tnzi.AI.Tests.Skills;

// ---------------------------------------------------------------------------
// Minimal SQLite DbContext for handler integration tests
// ---------------------------------------------------------------------------

public class SkillHandlerDbContext : TnziDbContext<SkillHandlerDbContext>
{
    public SkillHandlerDbContext(
        DbContextOptions<SkillHandlerDbContext> options,
        ICurrentUser currentUser)
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

// ---------------------------------------------------------------------------
// Test base: SQLite in-memory with EFCoreRepository<SkillHandlerDbContext, SkillEntity, Guid>
// ---------------------------------------------------------------------------

public abstract class SkillHandlerTestBase : IntegratedTestBase<SkillHandlerDbContext>
{
    protected readonly IRepository<SkillEntity, Guid> Repository;

    protected SkillHandlerTestBase()
    {
        Repository = ServiceProvider.GetRequiredService<IRepository<SkillEntity, Guid>>();
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        var config = new TypeAdapterConfig();
        MapperExtensions.SetMapper(new Mapper(config));

        services.AddScoped<IRepository<SkillEntity, Guid>,
            EFCoreRepository<SkillHandlerDbContext, SkillEntity, Guid>>();
    }

    protected async Task SeedAsync(params SkillEntity[] rows)
    {
        DbContext.Set<SkillEntity>().AddRange(rows);
        await DbContext.SaveChangesAsync();
    }

    protected async Task<SkillEntity?> ReloadAsync(Guid id)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<SkillEntity>().FindAsync(id);
    }
}

/// <summary>
/// SkillActivatedEventHandler unit tests - verifies that activation-count updates
/// are narrowed to the exact resolved row (slug + scope + tenantId + ownerUserId)
/// and do not bleed across tenants or users.
/// </summary>
public class SkillActivatedEventHandlerTests
{
    private static SkillActivatedEventHandler CreateHandler(
        IRepository<SkillEntity, Guid>? repository = null)
    {
        return new SkillActivatedEventHandler(
            Mock.Of<ILogger<SkillActivatedEventHandler>>(),
            repository);
    }

    // -------------------------------------------------------------------------
    // Source guard
    // -------------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_FileSystemSource_DoesNotAccessRepository()
    {
        var mockRepo = new Mock<IRepository<SkillEntity, Guid>>();
        var handler = CreateHandler(mockRepo.Object);

        var @event = new SkillActivatedEvent
        {
            Slug = "my-skill",
            Scope = SkillScope.Tenant,
            Source = SkillSource.FileSystem,
            ActivatedAt = DateTime.UtcNow
        };

        await handler.HandleAsync(@event);

        // Repository must never be touched for non-DB sources
        mockRepo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_NullRepository_DoesNotThrow()
    {
        var handler = CreateHandler(repository: null);

        var @event = new SkillActivatedEvent
        {
            Slug = "my-skill",
            Scope = SkillScope.Tenant,
            Source = SkillSource.Database,
            ActivatedAt = DateTime.UtcNow
        };

        // Should silently return - no repository registered
        await handler.HandleAsync(@event);
    }

    // -------------------------------------------------------------------------
    // Predicate narrowing: same slug, different user - only target row updated
    // -------------------------------------------------------------------------

    /// <summary>
    /// Two DB rows with same slug (User scope), different OwnerUserId.
    /// HandleAsync targeting ownerA must increment only rowA's ActivationCount.
    /// </summary>
    [Collection("SkillHandler_UserScope")]
    public class HandleAsync_UserScope_ActivationCount_OnlyTargetRowUpdated
        : SkillHandlerTestBase
    {
        [Fact]
        public async Task OnlyTargetRowIsUpdated()
        {
            var ownerA = Guid.NewGuid();
            var ownerB = Guid.NewGuid();

            var rowA = new SkillEntity
            {
                Id = Guid.NewGuid(),
                Slug = "shared-slug",
                Scope = SkillScope.User,
                OwnerUserId = ownerA,
                TenantId = null,
                Name = "Skill A",
                Content = "content",
                ActivationCount = 5,
                IsDeleted = false
            };

            var rowB = new SkillEntity
            {
                Id = Guid.NewGuid(),
                Slug = "shared-slug",
                Scope = SkillScope.User,
                OwnerUserId = ownerB,
                TenantId = null,
                Name = "Skill B",
                Content = "content",
                ActivationCount = 10,
                IsDeleted = false
            };

            await SeedAsync(rowA, rowB);

            var handler = new SkillActivatedEventHandler(
                Mock.Of<ILogger<SkillActivatedEventHandler>>(),
                Repository);

            var activatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var @event = new SkillActivatedEvent
            {
                Slug = "shared-slug",
                Scope = SkillScope.User,
                Source = SkillSource.Database,
                OwnerUserId = ownerA,
                SkillTenantId = null,
                ActivatedAt = activatedAt
            };

            await handler.HandleAsync(@event);

            // Reload from DB (clear tracker to avoid stale cache)
            var reloadedA = await ReloadAsync(rowA.Id);
            var reloadedB = await ReloadAsync(rowB.Id);

            reloadedA.ShouldNotBeNull();
            reloadedB.ShouldNotBeNull();

            // Only rowA should have been incremented
            reloadedA!.ActivationCount.ShouldBe(6, "ownerA's activation count should be incremented by 1");
            reloadedA.LastActivatedAt.ShouldBe(activatedAt);

            // rowB must be unchanged
            reloadedB!.ActivationCount.ShouldBe(10, "ownerB's activation count must not change");
            reloadedB.LastActivatedAt.ShouldBeNull();
        }
    }

    // -------------------------------------------------------------------------
    // Predicate narrowing: same slug, different tenant - only target row updated
    // -------------------------------------------------------------------------

    /// <summary>
    /// Two DB rows with same slug (Tenant scope), different TenantId.
    /// HandleAsync targeting tenantA must increment only rowA's ActivationCount.
    /// </summary>
    [Collection("SkillHandler_TenantScope")]
    public class HandleAsync_TenantScope_ActivationCount_OnlyTargetTenantRowUpdated
        : SkillHandlerTestBase
    {
        [Fact]
        public async Task OnlyTargetTenantRowIsUpdated()
        {
            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();

            var rowA = new SkillEntity
            {
                Id = Guid.NewGuid(),
                Slug = "shared-slug",
                Scope = SkillScope.Tenant,
                TenantId = tenantA,
                OwnerUserId = null,
                Name = "Skill A",
                Content = "content",
                ActivationCount = 3,
                IsDeleted = false
            };

            var rowB = new SkillEntity
            {
                Id = Guid.NewGuid(),
                Slug = "shared-slug",
                Scope = SkillScope.Tenant,
                TenantId = tenantB,
                OwnerUserId = null,
                Name = "Skill B",
                Content = "content",
                ActivationCount = 7,
                IsDeleted = false
            };

            await SeedAsync(rowA, rowB);

            var handler = new SkillActivatedEventHandler(
                Mock.Of<ILogger<SkillActivatedEventHandler>>(),
                Repository);

            var activatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var @event = new SkillActivatedEvent
            {
                Slug = "shared-slug",
                Scope = SkillScope.Tenant,
                Source = SkillSource.Database,
                SkillTenantId = tenantA,
                OwnerUserId = null,
                ActivatedAt = activatedAt
            };

            await handler.HandleAsync(@event);

            var reloadedA = await ReloadAsync(rowA.Id);
            var reloadedB = await ReloadAsync(rowB.Id);

            reloadedA.ShouldNotBeNull();
            reloadedB.ShouldNotBeNull();

            // Only rowA (tenantA) should be incremented
            reloadedA!.ActivationCount.ShouldBe(4, "tenantA's activation count should be incremented by 1");
            reloadedA.LastActivatedAt.ShouldBe(activatedAt);

            // rowB (tenantB) must be unchanged
            reloadedB!.ActivationCount.ShouldBe(7, "tenantB's activation count must not change");
            reloadedB.LastActivatedAt.ShouldBeNull();
        }
    }
}
