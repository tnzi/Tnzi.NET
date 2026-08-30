namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// 同一个 DI scope（同一个 DbContext）里连续保存**内容相同**的文件。
/// 按 MD5 去重这条路径的存在意义就是"相同内容会被反复命中"，所以它必须能被反复走：
/// 第一次调用把记录留在了变更跟踪器里，第二次命中去重分支时不能与它撞主键。
/// </summary>
public class SaveDedupWithinScopeTests : StorageIntegrationTestBase
{
    [Fact]
    public async Task SaveFromBytes_SameContentThreeTimesInOneScope_ReusesRecordAndCountsEachSave()
    {
        var service = CreateStorageService();
        var bytes = "same-content"u8.ToArray();

        var first = await service.SaveFromBytesAsync("a.txt", bytes, "text/plain");
        var second = await service.SaveFromBytesAsync("b.txt", bytes, "text/plain");
        var third = await service.SaveFromBytesAsync("c.txt", bytes, "text/plain");

        Assert.True(first.Succeeded, first.Message);
        Assert.True(second.Succeeded, second.Message);
        Assert.True(third.Succeeded, third.Message);

        Assert.Equal(first.Data!.Id, second.Data!.Id);
        Assert.Equal(first.Data.Id, third.Data!.Id);
        Assert.Equal(3, third.Data.ReferenceCount);

        // 物理文件只有一份：去重命中时不应再上传。
        Assert.Equal(1, await DbContext.FileRecords.CountAsync(f => f.Md5Hash == first.Data.Md5Hash));
    }

    /// <summary>
    /// 调用方开着事务时仓储延迟保存，去重分支读到的数据库快照必然落后于本 scope 内
    /// 已经发生的递增。计数在这种情况下依然要按次累加，否则清理任务会删掉仍被引用的文件。
    /// </summary>
    [Fact]
    public async Task SaveFromBytes_SameContentInsideTransaction_IncrementsReferenceCountPerSave()
    {
        var bytes = "content-already-stored"u8.ToArray();
        var seeded = await CreateStoredFileAsync("seed.txt", bytes);
        Assert.Equal(1, seeded.ReferenceCount);

        var service = CreateStorageService();

        await using var transaction = await DbContext.Database.BeginTransactionAsync();

        var first = await service.SaveFromBytesAsync("a.txt", bytes, "text/plain");
        var second = await service.SaveFromBytesAsync("b.txt", bytes, "text/plain");

        Assert.True(first.Succeeded, first.Message);
        Assert.True(second.Succeeded, second.Message);
        Assert.Equal(seeded.Id, second.Data!.Id);

        await DbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        var persisted = await DbContext.FileRecords.AsNoTracking().SingleAsync(f => f.Id == seeded.Id);
        Assert.Equal(3, persisted.ReferenceCount);
    }
}
