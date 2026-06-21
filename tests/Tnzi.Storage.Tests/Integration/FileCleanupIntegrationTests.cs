using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.EFCore;
using Tnzi.MultiTenancy;
using Tnzi.Storage.Helpers;

namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// FileCleanupService 集成测试（真实 SQLite + 真实物理存储），多租户「未启用」路径（默认配置）：
/// 覆盖 T4（过期分片会话清理）、T5（孤立引用验证器）及单租户回归。
/// 多租户启用下的真正隔离见 <see cref="FileCleanupMultiTenantTests"/>。
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
    // T4: 过期分片上传会话 + 残留分片清理
    // ------------------------------------------------------------------

    [Fact]
    public async Task CleanupExpiredUploadSessionsAsync_DeletesExpiredSessionAndChunks()
    {
        var (session, chunkPaths) = await SeedUploadSessionWithChunksAsync(null, expired: true);

        // 物理文件确实存在
        foreach (var path in chunkPaths)
        {
            Assert.True(await Storage.ExistsAsync(path), $"chunk file should exist before cleanup: {path}");
        }

        var service = CreateCleanupService();

        var deleted = await service.CleanupExpiredUploadSessionsAsync();

        Assert.Equal(1, deleted);
        Assert.Equal(0, await DbContext.FileUploadSessions.IgnoreQueryFilters().CountAsync(s => s.Id == session.Id));
        Assert.Equal(0, await DbContext.FileChunks.IgnoreQueryFilters().CountAsync(c => c.UploadSessionId == session.Id));

        // 物理分片文件也被删除
        foreach (var path in chunkPaths)
        {
            Assert.False(await Storage.ExistsAsync(path), $"chunk file should be deleted: {path}");
        }
    }

    [Fact]
    public async Task CleanupExpiredUploadSessionsAsync_KeepsNonExpiredSession()
    {
        var (session, _) = await SeedUploadSessionWithChunksAsync(null, expired: false);

        var service = CreateCleanupService();

        var deleted = await service.CleanupExpiredUploadSessionsAsync();

        Assert.Equal(0, deleted);
        Assert.Equal(1, await DbContext.FileUploadSessions.IgnoreQueryFilters().CountAsync(s => s.Id == session.Id));
        Assert.Equal(2, await DbContext.FileChunks.IgnoreQueryFilters().CountAsync(c => c.UploadSessionId == session.Id));
    }

    [Fact]
    public async Task CleanupAsync_IncludesExpiredSessionsInTotal()
    {
        await SeedUploadSessionWithChunksAsync(null, expired: true);

        var service = CreateCleanupService();

        var result = await service.CleanupAsync();

        Assert.True(result.Success);
        Assert.Equal(1, result.ExpiredSessionsDeleted);
        Assert.True(result.TotalDeleted >= 1);
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
            ReferenceCount = 1, // 先插入为 1，避免 EF HasDefaultValue(1) 把 CLR 默认 0 当成"未设置"
            CreationTime = DateTime.UtcNow.AddHours(-agedHours)
        };
        DbContext.FileRecords.Add(record);
        await DbContext.SaveChangesAsync();

        // 通过一次 UPDATE 显式写入目标 ReferenceCount（Modified 实体会发送精确值，0 才能落库）
        if (referenceCount != 1)
        {
            record.ReferenceCount = referenceCount;
            DbContext.FileRecords.Update(record);
            await DbContext.SaveChangesAsync();
        }

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

    private async Task<(FileUploadSession session, List<string> chunkPaths)> SeedUploadSessionWithChunksAsync(Guid? tenantId, bool expired)
    {
        var session = new FileUploadSession
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FileName = "big.zip",
            TotalSize = 6,
            ChunkSize = 3,
            TotalChunks = 2,
            CreationTime = DateTime.UtcNow.AddHours(-2),
            ExpiresAt = expired ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddHours(24)
        };
        DbContext.FileUploadSessions.Add(session);

        var paths = new List<string>();
        for (var i = 0; i < 2; i++)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"c{i}x"));
            var path = await Storage.UploadAsync($"chunk_{session.Id}_{i}.part", stream, "application/octet-stream");
            paths.Add(path);
            DbContext.FileChunks.Add(new FileChunk
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UploadSessionId = session.Id,
                ChunkIndex = i,
                ChunkSize = 3,
                ChunkPath = path,
                CreationTime = DateTime.UtcNow.AddHours(-2)
            });
        }

        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return (session, paths);
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
