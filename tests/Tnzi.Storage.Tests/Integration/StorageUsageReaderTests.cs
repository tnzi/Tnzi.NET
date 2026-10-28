using Tnzi.EFCore;

namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// 按范围读存储用量（按存储量计费用）：文件记录 + 缩略图 + 贡献者，整个安装不分创建人，用户范围只算他名下的。
/// </summary>
public class StorageUsageReaderTests : StorageIntegrationTestBase
{
    private static readonly Guid Alice = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Bob = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private StorageUsageReader CreateReader(params IStorageUsageContributor[] contributors)
        => new(
            new EFCoreRepository<StorageTestDbContext, FileRecord, Guid>(DbContext, serviceProvider: ServiceProvider),
            Storage,
            contributors,
            NullLogger<StorageUsageReader>.Instance);

    private async Task<FileRecord> SeedAsync(Guid? creator, long size, string? thumbnailPath = null, bool temporary = false)
    {
        var record = new FileRecord
        {
            Id = Guid.NewGuid(),
            FileName = $"{Guid.NewGuid():N}.bin",
            OriginalName = "file.bin",
            Extension = ".bin",
            ContentType = "application/octet-stream",
            Size = size,
            Path = $"{Guid.NewGuid():N}.bin",
            ThumbnailPath = thumbnailPath,
            IsTemporary = temporary,
            CreatorId = creator
        };
        DbContext.FileRecords.Add(record);
        await DbContext.SaveChangesAsync();
        await DbContext.FileRecords.Where(f => f.Id == record.Id).ExecuteUpdateAsync(s => s.SetProperty(f => f.CreatorId, creator));
        return record;
    }

    [Fact]
    public async Task TheWholeInstallation_CountsEveryRecord_IncludingSystemRenderedAndTemporaryOnes()
    {
        await SeedAsync(Alice, 100);
        await SeedAsync(Bob, 40, temporary: true);
        await SeedAsync(null, 7);

        Assert.Equal(147, await CreateReader().GetStoredBytesAsync(StorageUsageScope.Installation));
    }

    [Fact]
    public async Task AUser_CountsOnlyTheirOwnFiles()
    {
        await SeedAsync(Alice, 100);
        await SeedAsync(Bob, 40);

        Assert.Equal(100, await CreateReader().GetStoredBytesAsync(StorageUsageScope.ForUser(Alice)));
    }

    [Fact]
    public async Task Thumbnails_AreMeasuredOnce_AndCounted()
    {
        using var thumbnail = new MemoryStream(new byte[30]);
        var thumbnailPath = await Storage.UploadAsync("thumb.jpg", thumbnail, "image/jpeg");
        var withThumbnail = await SeedAsync(Alice, 100, thumbnailPath);
        var lostThumbnail = await SeedAsync(Alice, 10, "missing-thumbnail.jpg");

        Assert.Equal(140, await CreateReader().GetStoredBytesAsync(StorageUsageScope.Installation));

        DbContext.ChangeTracker.Clear();
        Assert.Equal(30, (await DbContext.FileRecords.FindAsync(withThumbnail.Id))!.ThumbnailSize);
        // 对象已不在：它不再占存储
        Assert.Equal(0, (await DbContext.FileRecords.FindAsync(lostThumbnail.Id))!.ThumbnailSize);

        // 记下之后不再向提供者量：删掉对象，数仍然是记下的那个
        await Storage.DeleteAsync(thumbnailPath);
        Assert.Equal(140, await CreateReader().GetStoredBytesAsync(StorageUsageScope.Installation));
    }

    [Fact]
    public async Task KnownSizesOnly_SumsWhatIsRecorded_WithoutAskingTheProvider()
    {
        using var thumbnail = new MemoryStream(new byte[30]);
        var thumbnailPath = await Storage.UploadAsync("thumb.jpg", thumbnail, "image/jpeg");
        var record = await SeedAsync(Alice, 100, thumbnailPath);

        Assert.Equal(100, await CreateReader().GetStoredBytesAsync(StorageUsageScope.Installation, StorageUsageReadMode.KnownSizesOnly));

        DbContext.ChangeTracker.Clear();
        Assert.Null((await DbContext.FileRecords.FindAsync(record.Id))!.ThumbnailSize);
    }

    [Fact]
    public async Task Contributors_AddTheirBytes_AndOnesThatOnlyKnowUsersAnswerZeroForTheInstallation()
    {
        await SeedAsync(Alice, 100);
        var perUserOnly = new PerUserContributor(Alice, 25);

        Assert.Equal(125, await CreateReader(perUserOnly).GetStoredBytesAsync(StorageUsageScope.ForUser(Alice)));
        Assert.Equal(100, await CreateReader(perUserOnly).GetStoredBytesAsync(StorageUsageScope.Installation));
    }

    private sealed class PerUserContributor(Guid user, long bytes) : IStorageUsageContributor
    {
        public Task<long> GetUsedBytesAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(userId == user ? bytes : 0L);
    }
}
