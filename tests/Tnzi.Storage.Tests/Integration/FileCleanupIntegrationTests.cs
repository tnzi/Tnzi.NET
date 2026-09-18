using Tnzi.EFCore;

namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// FileCleanupService 集成测试（真实 SQLite + 真实物理存储），多租户「未启用」路径（默认配置）：
/// 覆盖 T5（孤立引用验证器）及单租户回归。
/// 多租户启用下的真正隔离见 <see cref="FileCleanupMultiTenantTests"/>。
/// T4（过期分片会话清理）随两张表搬到了 Tnzi.Storage.Workspace.Tests —— 它现在是一个
/// <c>IStorageCleanupContributor</c>，本项目里没有任何贡献者，那正是「没加载工作区包」的样子。
/// </summary>
public class FileCleanupIntegrationTests : StorageIntegrationTestBase
{
    // ------------------------------------------------------------------
    // 单租户回归（MT 未启用）：删孤儿、保留被引用文件
    // ------------------------------------------------------------------

    [Fact]
    public async Task CleanupOrphanFilesAsync_DeletesOrphan_KeepsReferenced()
    {
        // 一个过期孤儿（ReferenceCount=0，应删）
        await SeedOrphanFileAsync(null, "orphan.txt", agedHours: 200, referenceCount: 0);
        // 一个仍被引用的文件（ReferenceCount=1，不应删）
        var keep = await SeedOrphanFileAsync(null, "keep.txt", agedHours: 200, referenceCount: 1);

        var service = CreateCleanupService();

        var deleted = await service.CleanupOrphanFilesAsync();

        Assert.Equal(1, deleted);
        var survivors = await DbContext.FileRecords.IgnoreQueryFilters().ToListAsync();
        Assert.Single(survivors);
        Assert.Equal(keep.Id, survivors[0].Id);
    }

    [Fact]
    public async Task CleanupOrphanFilesAsync_DoesNotThrow_WhenNoCandidates()
    {
        var service = CreateCleanupService();
        var deleted = await service.CleanupOrphanFilesAsync();
        Assert.Equal(0, deleted);
    }

    // ------------------------------------------------------------------
    // 「没加载 Tnzi.Storage.Workspace」的现场：一个贡献者都没有。
    // 本测试项目不引用那个包，所以这不是模拟出来的现场。
    // ------------------------------------------------------------------

    [Fact]
    public async Task RunContributorsAsync_ReturnsZero_WhenNoContributorRegistered()
    {
        var service = CreateCleanupService();

        var deleted = await service.RunContributorsAsync();

        Assert.Equal(0, deleted);
    }

    [Fact]
    public async Task CleanupAsync_Succeeds_WhenNoContributorRegistered()
    {
        // 过期分片会话那一趟原本写死在服务里、直接吃两个属于工作区的仓储。
        // 没有本用例时，「构造参数解析不出来 → 宿主启动即崩」这种回归不会被任何测试抓到。
        await SeedOrphanFileAsync(null, "orphan.txt", agedHours: 200, referenceCount: 0);

        var service = CreateCleanupService();

        var result = await service.CleanupAsync();

        Assert.True(result.Success);
        Assert.Empty(result.Errors);
        Assert.Equal(0, result.ContributedDeleted);
        Assert.Equal(1, result.OrphanFilesDeleted);
    }

    [Fact]
    public async Task CleanupAsync_KeepsRunning_WhenAContributorThrows()
    {
        // 一个坏掉的贡献者只该让自己那一趟失败：父模块自己的三趟以及别的贡献者照跑。
        await SeedOrphanFileAsync(null, "orphan.txt", agedHours: 200, referenceCount: 0);

        var service = CreateCleanupService(contributors: [new ThrowingContributor(), new CountingContributor(3)]);

        var result = await service.CleanupAsync();

        Assert.True(result.Success);
        Assert.Equal(3, result.ContributedDeleted);
        Assert.Equal(1, result.OrphanFilesDeleted);
        Assert.Contains(result.Errors, e => e.Contains("Boom", StringComparison.Ordinal));
    }

    private sealed class ThrowingContributor : IStorageCleanupContributor
    {
        public string Name => "Throwing";
        public Task<int> CleanupAsync(int maxItems, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Boom");
    }

    private sealed class CountingContributor(int deleted) : IStorageCleanupContributor
    {
        public string Name => "Counting";
        public Task<int> CleanupAsync(int maxItems, CancellationToken cancellationToken = default)
            => Task.FromResult(deleted);
    }

    // ------------------------------------------------------------------
    // T5: 默认孤立引用验证器
    // ------------------------------------------------------------------

    [Fact]
    public async Task Validator_ReturnsTrue_WhenEntityExists()
    {
        var existing = await CreateStoredFileAsync("existing.txt", "x"u8.ToArray());
        var validator = CreateValidatorFor(typeof(FileRecord));

        var exists = await validator.IsEntityExistsAsync("FileRecord", existing.Id);

        Assert.True(exists);
    }

    [Fact]
    public async Task Validator_ReturnsFalse_WhenEntityMissing()
    {
        var validator = CreateValidatorFor(typeof(FileRecord));

        var exists = await validator.IsEntityExistsAsync("FileRecord", Guid.NewGuid());

        Assert.False(exists);
    }

    [Fact]
    public async Task Validator_ReturnsTrue_WhenTypeUnresolvable_Conservative()
    {
        var validator = CreateValidatorFor(typeof(FileRecord));

        // 未注册的类型名 → 无法解析 → 保守视为存在，绝不误删
        var exists = await validator.IsEntityExistsAsync("TotallyUnknownEntity", Guid.NewGuid());

        Assert.True(exists);
    }

    [Fact]
    public async Task Validator_ReturnsTrue_WhenEntityTypeNullOrEmpty_Conservative()
    {
        var validator = CreateValidatorFor(typeof(FileRecord));

        Assert.True(await validator.IsEntityExistsAsync("", Guid.NewGuid()));
        Assert.True(await validator.IsEntityExistsAsync("   ", Guid.NewGuid()));
    }

    [Fact]
    public async Task CleanupOrphanReferencesAsync_DeletesReference_WhenEntityMissing()
    {
        StorageOptions.Cleanup.EnableOrphanReferenceCleanup = true;

        var file = await SeedOrphanFileAsync(null, "ref-file.txt", agedHours: 0, referenceCount: 1);
        // 引用指向一个不存在的实体（FileRecord + 随机 Id），且引用已过保留期
        await SeedReferenceAsync(null, file.Id, "FileRecord", Guid.NewGuid(), agedHours: 200);

        var validator = CreateValidatorFor(typeof(FileRecord));
        var service = CreateCleanupService(orphanReferenceValidator: validator);

        var deleted = await service.CleanupOrphanReferencesAsync();

        Assert.Equal(1, deleted);
        Assert.Equal(0, await DbContext.FileReferences.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task CleanupOrphanReferencesAsync_KeepsReference_WhenEntityExists()
    {
        StorageOptions.Cleanup.EnableOrphanReferenceCleanup = true;

        var file = await SeedOrphanFileAsync(null, "ref-file2.txt", agedHours: 0, referenceCount: 1);
        // 引用指向一个存在的实体（file 本身），引用已过保留期 → 实体存在 → 保留
        await SeedReferenceAsync(null, file.Id, "FileRecord", file.Id, agedHours: 200);

        var validator = CreateValidatorFor(typeof(FileRecord));
        var service = CreateCleanupService(orphanReferenceValidator: validator);

        var deleted = await service.CleanupOrphanReferencesAsync();

        Assert.Equal(0, deleted);
        Assert.Equal(1, await DbContext.FileReferences.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task CleanupOrphanReferencesAsync_ReturnsZero_WhenNoValidatorRegistered()
    {
        StorageOptions.Cleanup.EnableOrphanReferenceCleanup = true;
        var service = CreateCleanupService(orphanReferenceValidator: null);

        var deleted = await service.CleanupOrphanReferencesAsync();

        Assert.Equal(0, deleted);
    }

    // ------------------------------------------------------------------
    // Seed helpers
    // ------------------------------------------------------------------

    private async Task<FileRecord> SeedOrphanFileAsync(Guid? tenantId, string name, int agedHours, int referenceCount = 0)
    {
        using var stream = new MemoryStream("orphan"u8.ToArray());
        var savedPath = await Storage.UploadAsync(name, stream, "text/plain");
        var record = new FileRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FileName = name,
            OriginalName = name,
            Extension = Path.GetExtension(name),
            Size = 6,
            Path = savedPath,
            Provider = Storage.ProviderName,
            ContentType = "text/plain",
            // 直接以目标计数插入：0 能落库是 FileRecordConfiguration 的哨兵保证的（此前这里要先插 1
            // 再 UPDATE 成 0 —— 那是在替模型缺陷打补丁，而生产代码没有这层补丁）。
            ReferenceCount = referenceCount,
            CreationTime = DateTime.UtcNow.AddHours(-agedHours)
        };
        DbContext.FileRecords.Add(record);
        await DbContext.SaveChangesAsync();

        // 清空变更跟踪，模拟一次新请求；否则清理服务加载的 no-track 副本在 Remove 时会与已跟踪实例冲突
        DbContext.ChangeTracker.Clear();
        return record;
    }

    private async Task SeedReferenceAsync(Guid? tenantId, Guid fileId, string entityType, Guid entityId, int agedHours)
    {
        var reference = new FileReference
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FileId = fileId,
            EntityType = entityType,
            EntityId = entityId,
            FieldName = "Test",
            IsTemporary = false,
            CreationTime = DateTime.UtcNow.AddHours(-agedHours)
        };
        DbContext.FileReferences.Add(reference);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    /// <summary>
    /// 构造一个 EntityManagerOrphanReferenceValidator，其 IEntityManager 用 Mock 把给定 CLR 类型
    /// 映射到测试 DbContext（StorageTestDbContext），并让 ServiceProvider 解析出真实的测试 DbContext。
    /// </summary>
    private EntityManagerOrphanReferenceValidator CreateValidatorFor(params Type[] entityTypes)
    {
        var entityManager = new Mock<IEntityManager>();
        entityManager.Setup(m => m.GetAllEntityTypes()).Returns(entityTypes);
        foreach (var t in entityTypes)
        {
            entityManager.Setup(m => m.GetDbContextTypeForEntity(t)).Returns(typeof(StorageTestDbContext));
        }
        entityManager
            .Setup(m => m.GetDbContextTypeForEntity(It.Is<Type>(x => !entityTypes.Contains(x))))
            .Throws(new InvalidOperationException("not registered"));

        return new EntityManagerOrphanReferenceValidator(
            entityManager.Object,
            ServiceProvider,
            NullLogger<EntityManagerOrphanReferenceValidator>.Instance);
    }
}
