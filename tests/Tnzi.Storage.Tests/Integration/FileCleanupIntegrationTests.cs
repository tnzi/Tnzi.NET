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

    /// <summary>
    /// 归档进目录的临时记录不是孤儿。这类行来自「文件库直传落临时记录、挪进目录只改 FolderId」那段时期：
    /// 没有任何引用行，计数为 0，开启清理后过了保留期就连同缩略图一起被删掉。
    /// 现在挪动时会转正，这一条守的是之前已经躺在目录里的那些行。
    /// </summary>
    [Fact]
    public async Task CleanupOrphanFilesAsync_KeepsATemporaryRecordFiledInAFolder()
    {
        var filed = await SeedOrphanFileAsync(null, "filed.txt", agedHours: 200, referenceCount: 0, isTemporary: true, folderId: Guid.NewGuid());
        // 正控制：同样过期、同样计数为 0、没归档的临时上传（被放弃的头像草稿就是这样）照常回收
        await SeedOrphanFileAsync(null, "draft.txt", agedHours: 200, referenceCount: 0, isTemporary: true);

        var deleted = await CreateCleanupService().CleanupOrphanFilesAsync();

        Assert.Equal(1, deleted);
        var survivors = await DbContext.FileRecords.IgnoreQueryFilters().ToListAsync();
        Assert.Single(survivors);
        Assert.Equal(filed.Id, survivors[0].Id);
        Assert.True(await Storage.ExistsAsync(filed.Path!));
    }

    // ------------------------------------------------------------------
    // 临时文件：判据是记录本身，不是引用行
    // ------------------------------------------------------------------

    /// <summary>
    /// ★★ <c>POST files/upload</c> 与分片上传落下的临时记录一条引用行都没有。此前这一趟只遍历临时引用行，
    /// <c>TemporaryFileRetentionHours</c> 对它们从不生效。
    /// </summary>
    [Fact]
    public async Task CleanupTemporaryFilesAsync_RemovesAnAbandonedUpload_ThatHasNoReferenceRow()
    {
        StorageOptions.Cleanup.TemporaryFileRetentionHours = 24;
        var abandoned = await SeedOrphanFileAsync(null, "abandoned.txt", agedHours: 48, referenceCount: 0, isTemporary: true);
        var fresh = await SeedOrphanFileAsync(null, "fresh.txt", agedHours: 2, referenceCount: 0, isTemporary: true);
        var filed = await SeedOrphanFileAsync(null, "filed.txt", agedHours: 48, referenceCount: 0, isTemporary: true, folderId: Guid.NewGuid());
        var bound = await SeedOrphanFileAsync(null, "bound.txt", agedHours: 48, referenceCount: 1, isTemporary: true);
        // 正式记录计数归零是孤儿回收的事（它有自己的保留期），临时清理不碰。
        var permanent = await SeedOrphanFileAsync(null, "permanent.txt", agedHours: 48, referenceCount: 0);

        var deleted = await CreateCleanupService().CleanupTemporaryFilesAsync();

        Assert.Equal(1, deleted);
        var survivors = await DbContext.FileRecords.IgnoreQueryFilters().Select(f => f.Id).ToListAsync();
        Assert.DoesNotContain(abandoned.Id, survivors);
        Assert.False(await Storage.ExistsAsync(abandoned.Path!));
        Assert.Equal(new[] { fresh.Id, filed.Id, bound.Id, permanent.Id }.OrderBy(id => id), survivors.OrderBy(id => id));
    }

    // ------------------------------------------------------------------
    // 孤立引用：每轮只查 MaxFilesPerRun 条，必须一轮一轮往后推进
    // ------------------------------------------------------------------

    /// <summary>
    /// ★★ 实体还在的引用查完不会被删。没有游标时每轮取到的是同一批有效引用，排在后面的真孤立引用永远轮不到。
    /// </summary>
    [Fact]
    public async Task CleanupOrphanReferencesAsync_ReachesAnOrphanBehindMoreValidReferencesThanOneRunChecks()
    {
        StorageOptions.Cleanup.EnableOrphanReferenceCleanup = true;
        StorageOptions.Cleanup.MaxFilesPerRun = 2;

        var file = await SeedOrphanFileAsync(null, "shared.txt", agedHours: 0, referenceCount: 6);
        for (var i = 0; i < 5; i++)
        {
            // 每条有效引用指向一个真实存在的实体（这里借文件记录当实体）。
            var holder = await SeedOrphanFileAsync(null, $"holder-{i}.txt", agedHours: 0, referenceCount: 1);
            await SeedReferenceAsync(null, file.Id, "FileRecord", holder.Id, agedHours: 300);
        }
        // 插在最后、也最新（仍过保留期）：不论按插入顺序还是按创建时间，它都排在那批有效引用后面。
        await SeedReferenceAsync(null, file.Id, "FileRecord", Guid.NewGuid(), agedHours: 100);

        var validator = CreateValidatorFor(typeof(FileRecord));
        var deleted = 0;
        for (var run = 0; run < 6 && deleted == 0; run++)
            deleted += await CreateCleanupService(orphanReferenceValidator: validator).CleanupOrphanReferencesAsync();

        Assert.Equal(1, deleted);
        Assert.Equal(5, await DbContext.FileReferences.IgnoreQueryFilters().CountAsync());
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

    // ------------------------------------------------------------------
    // 删不掉的对象：交给删除事件的重试，而不是记一条日志就放手
    // ------------------------------------------------------------------

    /// <summary>
    /// ★★ 记录在删对象之前就已条件删除，删不掉的对象再也没有记录可以找回，下一趟按记录枚举永远看不见它。
    /// 内置 provider 故障时报 <c>false</c> 而不抛，此前这里连返回值都不看：对象就此成为永久孤儿。
    /// </summary>
    [Fact]
    public async Task CleanupOrphanFilesAsync_HandsAnObjectItCouldNotDelete_ToTheDeleteRetry()
    {
        var orphan = await SeedOrphanFileAsync(null, "stuck.txt", agedHours: 200, referenceCount: 0);
        var eventBus = Mock.Get(ServiceProvider.GetRequiredService<Tnzi.EventBus.IEventBus>());

        var deleted = await CreateCleanupService(storage: new RefusingDeleteStorage(Storage)).CleanupOrphanFilesAsync();

        Assert.Equal(1, deleted);
        Assert.True(await Storage.ExistsAsync(orphan.Path!));
        eventBus.Verify(b => b.PublishAsync(
            It.Is<Tnzi.Storage.Events.FileDeleteRequestedEvent>(e => e.FileId == orphan.Id && e.FilePath == orphan.Path && e.ThumbnailPath == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>对照：删掉了就不再请求重试。</summary>
    [Fact]
    public async Task CleanupOrphanFilesAsync_RequestsNoRetry_WhenTheObjectIsGone()
    {
        var orphan = await SeedOrphanFileAsync(null, "gone.txt", agedHours: 200, referenceCount: 0);
        var eventBus = Mock.Get(ServiceProvider.GetRequiredService<Tnzi.EventBus.IEventBus>());

        var deleted = await CreateCleanupService().CleanupOrphanFilesAsync();

        Assert.Equal(1, deleted);
        Assert.False(await Storage.ExistsAsync(orphan.Path!));
        eventBus.Verify(b => b.PublishAsync(It.IsAny<Tnzi.Storage.Events.FileDeleteRequestedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>内置 provider 的故障形态：删除报 <c>false</c>、不抛，对象原样留着。</summary>
    private sealed class RefusingDeleteStorage(IFileStorage inner) : IFileStorage
    {
        public string ProviderName => inner.ProviderName;
        public Task<string> UploadAsync(string fileName, Stream stream, string? contentType = null) => inner.UploadAsync(fileName, stream, contentType);
        public Task<Stream> DownloadAsync(string filePath) => inner.DownloadAsync(filePath);
        public Task<bool> DeleteAsync(string filePath) => Task.FromResult(false);
        public Task<bool> ExistsAsync(string filePath) => inner.ExistsAsync(filePath);
        public Task<string> GetUrlAsync(string filePath, int? expiresIn = null) => inner.GetUrlAsync(filePath, expiresIn);
        public Task<long> GetFileSizeAsync(string filePath) => inner.GetFileSizeAsync(filePath);
        public Task<(Stream Stream, long Start, long End, long TotalLength)> DownloadRangeAsync(string filePath, long? rangeStart = null, long? rangeEnd = null)
            => inner.DownloadRangeAsync(filePath, rangeStart, rangeEnd);
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

    private async Task<FileRecord> SeedOrphanFileAsync(
        Guid? tenantId, string name, int agedHours, int referenceCount = 0, bool isTemporary = false, Guid? folderId = null)
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
            IsTemporary = isTemporary,
            FolderId = folderId,
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
