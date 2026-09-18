using Tnzi.EFCore;

namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// 引用计数「同步 / 校验」只对临时记录有完整的信息来源。
/// </summary>
/// <remarks>
/// <c>ReferenceCount</c> 混着两种引用：显式的 <c>Storage_Reference</c> 行，与**隐式**的持有者
/// （正式上传从 1 起、MD5 复用每命中一次 +1 —— 这两种都不产生引用行）。只按引用行重算，
/// 会把每一条未绑定的正式上传与 MD5 共享文件归零，默认开启的孤儿回收随后物理删除它们。
/// 因此：批量同步只碰 <c>IsTemporary = true</c> 的记录；单条同步对正式记录拒绝；
/// 校验对正式记录只要求「不低于引用行数」。
/// </remarks>
public class ReferenceCountSyncTests : StorageIntegrationTestBase
{
    private FileReferenceService CreateReferenceService()
    {
        return new FileReferenceService(
            new EFCoreRepository<StorageTestDbContext, FileRecord, Guid>(DbContext, serviceProvider: ServiceProvider),
            new EFCoreRepository<StorageTestDbContext, FileReference, Guid>(DbContext, serviceProvider: ServiceProvider),
            ServiceProvider);
    }

    private async Task<FileRecord> SeedRecordAsync(string name, bool isTemporary, int referenceCount, int agedHours = 0)
    {
        using var stream = new MemoryStream("payload"u8.ToArray());
        var savedPath = await Storage.UploadAsync(name, stream, "text/plain");
        var record = new FileRecord
        {
            Id = Guid.NewGuid(),
            FileName = name,
            OriginalName = name,
            Extension = Path.GetExtension(name),
            Size = 7,
            Path = savedPath,
            Provider = Storage.ProviderName,
            ContentType = "text/plain",
            IsTemporary = isTemporary,
            ReferenceCount = referenceCount,
            CreationTime = DateTime.UtcNow.AddHours(-agedHours)
        };
        DbContext.FileRecords.Add(record);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return record;
    }

    private async Task SeedReferenceAsync(Guid fileId, bool isTemporary = false)
    {
        DbContext.FileReferences.Add(new FileReference
        {
            Id = Guid.NewGuid(),
            FileId = fileId,
            EntityType = "Doc",
            EntityId = Guid.NewGuid(),
            FieldName = "Attachment",
            IsTemporary = isTemporary
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private async Task<int> ReloadReferenceCountAsync(Guid id)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.FileRecords.AsNoTracking()
            .Where(f => f.Id == id)
            .Select(f => f.ReferenceCount)
            .SingleAsync();
    }

    // ------------------------------------------------------------------
    // 批量同步
    // ------------------------------------------------------------------

    [Fact]
    public async Task SyncAllReferenceCounts_LeavesAnUnboundPermanentUploadAtOne()
    {
        // 文件管理器的分片上传 / 消费方直接 SaveAsync 的正式记录：从 1 起、没有引用行。
        var upload = await SeedRecordAsync("report.pdf", isTemporary: false, referenceCount: 1, agedHours: 200);

        var result = await CreateReferenceService().SyncAllReferenceCountsAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Data);
        Assert.Equal(1, await ReloadReferenceCountAsync(upload.Id));

        // 同步之后孤儿回收不能把它当作孤儿。
        var deleted = await CreateCleanupService().CleanupOrphanFilesAsync();
        Assert.Equal(0, deleted);
        Assert.True(await Storage.ExistsAsync(upload.Path!));
    }

    [Fact]
    public async Task SyncAllReferenceCounts_LeavesAnMd5SharedFileAtItsHolderCount()
    {
        // 两个用户上传了相同字节：一条记录、两个隐式持有者、零引用行。
        var shared = await SeedRecordAsync("shared.txt", isTemporary: false, referenceCount: 2);

        var result = await CreateReferenceService().SyncAllReferenceCountsAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(2, await ReloadReferenceCountAsync(shared.Id));
    }

    [Fact]
    public async Task SyncAllReferenceCounts_RecomputesTemporaryRecordsFromTheirReferenceRows()
    {
        // 修复哨兵之前创建的临时记录多算了 1（没有引用行却是 1）。
        var stale = await SeedRecordAsync("stale-temp.txt", isTemporary: true, referenceCount: 1);
        // 一条被绑定过却少算的临时记录（一行正式引用、计数 0）。
        var undercounted = await SeedRecordAsync("bound-temp.txt", isTemporary: true, referenceCount: 0);
        await SeedReferenceAsync(undercounted.Id);
        // 临时引用行不算数。
        var pendingOnly = await SeedRecordAsync("pending-temp.txt", isTemporary: true, referenceCount: 0);
        await SeedReferenceAsync(pendingOnly.Id, isTemporary: true);
        // 已经一致的临时记录不计入返回值。
        var consistent = await SeedRecordAsync("ok-temp.txt", isTemporary: true, referenceCount: 1);
        await SeedReferenceAsync(consistent.Id);

        var result = await CreateReferenceService().SyncAllReferenceCountsAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data);
        Assert.Equal(0, await ReloadReferenceCountAsync(stale.Id));
        Assert.Equal(1, await ReloadReferenceCountAsync(undercounted.Id));
        Assert.Equal(0, await ReloadReferenceCountAsync(pendingOnly.Id));
        Assert.Equal(1, await ReloadReferenceCountAsync(consistent.Id));
    }

    [Fact]
    public async Task SyncAllReferenceCounts_KeepsAFileWhosePermanentUploadReusedATemporaryRecord()
    {
        // 默认上传端点先以临时形态存了一份；之后另一个调用方把相同字节作为正式上传交进来，
        // MD5 去重命中同一条记录 —— 它现在有一个隐式持有者、零引用行。同步不能把它归零，
        // 孤儿回收也不能删掉正式上传者的文件。
        var storageService = CreateStorageService();
        var payload = "shared bytes"u8.ToArray();
        using (var first = new MemoryStream(payload))
        {
            var temp = await storageService.SaveAsync("draft.txt", first, isTemporary: true);
            Assert.True(temp.Succeeded);
        }
        using (var second = new MemoryStream(payload))
        {
            var permanent = await storageService.SaveAsync("final.txt", second, isTemporary: false);
            Assert.True(permanent.Succeeded);
            Assert.Equal("File reused by MD5", permanent.Message);
        }

        var shared = await DbContext.FileRecords.AsNoTracking().SingleAsync();
        await DbContext.FileRecords
            .Where(f => f.Id == shared.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.CreationTime, DateTime.UtcNow.AddHours(-200)));

        var result = await CreateReferenceService().SyncAllReferenceCountsAsync();

        Assert.True(result.Succeeded);
        Assert.True(await ReloadReferenceCountAsync(shared.Id) >= 1);

        var deleted = await CreateCleanupService().CleanupOrphanFilesAsync();
        Assert.Equal(0, deleted);
        Assert.True(await Storage.ExistsAsync(shared.Path!));
    }

    // ------------------------------------------------------------------
    // 单条同步
    // ------------------------------------------------------------------

    [Fact]
    public async Task SyncReferenceCount_OnAPermanentUpload_IsRefused()
    {
        var upload = await SeedRecordAsync("keep.txt", isTemporary: false, referenceCount: 1);

        var result = await CreateReferenceService().SyncReferenceCountAsync(upload.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.Equal(1, await ReloadReferenceCountAsync(upload.Id));
    }

    [Fact]
    public async Task SyncReferenceCount_OnATemporaryRecord_RecomputesFromReferenceRows()
    {
        var temp = await SeedRecordAsync("temp.txt", isTemporary: true, referenceCount: 5);
        await SeedReferenceAsync(temp.Id);
        await SeedReferenceAsync(temp.Id);

        var result = await CreateReferenceService().SyncReferenceCountAsync(temp.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data);
        Assert.Equal(2, await ReloadReferenceCountAsync(temp.Id));
    }

    // ------------------------------------------------------------------
    // 校验
    // ------------------------------------------------------------------

    [Fact]
    public async Task ValidateReferenceCount_OnAnUnboundPermanentUpload_IsValid()
    {
        // 不能把每一条未绑定的正式上传报成「不一致」再引导管理员去同步。
        var upload = await SeedRecordAsync("keep.txt", isTemporary: false, referenceCount: 1);

        var result = await CreateReferenceService().ValidateReferenceCountAsync(upload.Id);

        Assert.True(result.Succeeded);
        Assert.True(result.Data);
    }

    [Fact]
    public async Task ValidateReferenceCount_OnAPermanentUploadBelowItsReferenceRows_IsInvalid()
    {
        // 计数低于引用行数是唯一能确定的不一致：解绑时一定会把还在用的文件删掉。
        var upload = await SeedRecordAsync("keep.txt", isTemporary: false, referenceCount: 1);
        await SeedReferenceAsync(upload.Id);
        await SeedReferenceAsync(upload.Id);

        var result = await CreateReferenceService().ValidateReferenceCountAsync(upload.Id);

        Assert.True(result.Succeeded);
        Assert.False(result.Data);
    }

    [Fact]
    public async Task ValidateReferenceCount_OnATemporaryRecord_RequiresAnExactMatch()
    {
        var temp = await SeedRecordAsync("temp.txt", isTemporary: true, referenceCount: 1);

        var result = await CreateReferenceService().ValidateReferenceCountAsync(temp.Id);

        Assert.True(result.Succeeded);
        Assert.False(result.Data);
    }
}
