using System.IO.Compression;

namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// `FileRecord.ReferenceCount = 0` 必须真的落库为 0。
/// </summary>
/// <remarks>
/// 列上配了 <c>HasDefaultValue(1)</c> 而没有配哨兵：EF Core 把 CLR 默认值 0 当成「没设置」，
/// 插入时省掉这一列让数据库默认值 1 赢。于是服务层每一处刻意写的 <c>ReferenceCount = 0</c>
/// （临时上传 / 复制 / 压缩 / 解压 / 分片上传完成）在每个 provider 上都静默落成 1 ——
/// 这些记录从来不是孤儿，`Storage:Cleanup` 的孤儿回收对它们整体失效，而代码、注释与文档
/// 都说它们「引用计数从 0 起」。这里从数据库回读（清掉变更跟踪器，不看内存里那份）钉住每一条写路径。
/// </remarks>
public class ReferenceCountPersistenceTests : StorageIntegrationTestBase
{
    private async Task<int> ReloadReferenceCountAsync(Guid id)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.FileRecords.AsNoTracking()
            .Where(f => f.Id == id)
            .Select(f => f.ReferenceCount)
            .SingleAsync();
    }

    [Fact]
    public async Task InsertingARecordWithZeroReferences_PersistsZero()
    {
        var record = new FileRecord
        {
            Id = Guid.NewGuid(),
            FileName = "orphan.txt",
            Extension = ".txt",
            Provider = Storage.ProviderName,
            ReferenceCount = 0
        };
        DbContext.FileRecords.Add(record);
        await DbContext.SaveChangesAsync();

        Assert.Equal(0, await ReloadReferenceCountAsync(record.Id));
    }

    [Fact]
    public async Task InsertingARecordWithoutTouchingReferenceCount_StillPersistsOne()
    {
        // 哨兵只是让 0 变得可表达，不能把「没人碰过」的记录改成 0。
        var record = new FileRecord
        {
            Id = Guid.NewGuid(),
            FileName = "permanent.txt",
            Extension = ".txt",
            Provider = Storage.ProviderName
        };
        DbContext.FileRecords.Add(record);
        await DbContext.SaveChangesAsync();

        Assert.Equal(1, await ReloadReferenceCountAsync(record.Id));
    }

    [Fact]
    public async Task SaveAsync_Temporary_PersistsZeroReferences()
    {
        var service = CreateStorageService();

        var result = await service.SaveAsync("temp.txt", new MemoryStream("temp"u8.ToArray()), isTemporary: true);

        Assert.True(result.Succeeded);
        Assert.Equal(0, await ReloadReferenceCountAsync(result.Data!.Id));
    }

    [Fact]
    public async Task SaveAsync_Permanent_PersistsOneReference()
    {
        var service = CreateStorageService();

        var result = await service.SaveAsync("keep.txt", new MemoryStream("keep"u8.ToArray()), isTemporary: false);

        Assert.True(result.Succeeded);
        Assert.Equal(1, await ReloadReferenceCountAsync(result.Data!.Id));
    }

    [Fact]
    public async Task CopyAsync_PersistsZeroReferencesOnTheCopy()
    {
        var service = CreateStorageService();
        var source = await CreateStoredFileAsync("source.txt", "source"u8.ToArray());

        var result = await service.CopyAsync(source.Id, "copy.txt");

        Assert.True(result.Succeeded);
        Assert.Equal(0, await ReloadReferenceCountAsync(result.Data!.Id));
    }

    [Fact]
    public async Task CompressAsync_PersistsZeroReferencesOnTheArchive()
    {
        var service = CreateStorageService();
        var a = await CreateStoredFileAsync("a.txt", [1, 2, 3]);

        var result = await service.CompressAsync([a.Id], "bundle.zip");

        Assert.True(result.Succeeded);
        Assert.Equal(0, await ReloadReferenceCountAsync(result.Data!.Id));
    }

    [Fact]
    public async Task DecompressAsync_PersistsZeroReferencesOnEveryExtractedFile()
    {
        var service = CreateStorageService();
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in new[] { "one.txt", "two.txt" })
            {
                using var entry = archive.CreateEntry(name).Open();
                entry.Write("x"u8);
            }
        }
        var zip = await CreateStoredFileAsync("bundle.zip", buffer.ToArray());

        var result = await service.DecompressAsync(zip.Id);

        Assert.True(result.Succeeded);
        var extracted = result.Data!.Select(f => f.Id).ToList();
        Assert.Equal(2, extracted.Count);
        foreach (var id in extracted)
            Assert.Equal(0, await ReloadReferenceCountAsync(id));
    }
}
