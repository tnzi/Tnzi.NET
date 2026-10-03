namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// 留在父模块的核心服务集成用例。
/// </summary>
/// <remarks>
/// 这两条原先与版本 / 分享 / 分片上传的用例同住一个类，但它们测的是
/// <c>FileStorageService</c> 自己的行为，跟工作区包一点关系都没有 ——
/// 随那个类整体搬走会让父模块自己丢掉这份覆盖。
/// </remarks>
public class StorageCoreServiceTests : StorageIntegrationTestBase
{
    [Fact]
    public async Task BatchVerifyIntegrityAsync_ReturnsSummary_WithProblemsOnly()
    {
        var service = CreateStorageService();
        await CreateStoredFileAsync("healthy.txt", "healthy"u8.ToArray());
        var missing = new FileRecord
        {
            Id = Guid.NewGuid(),
            FileName = "missing.txt",
            OriginalName = "missing.txt",
            Extension = ".txt",
            Size = 5,
            Path = "missing/path.txt",
            Md5Hash = "deadbeef",
            Provider = Storage.ProviderName,
            ContentType = "text/plain"
        };
        DbContext.FileRecords.Add(missing);
        await DbContext.SaveChangesAsync();

        var result = await service.BatchVerifyIntegrityAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.TotalChecked);
        Assert.Equal(1, result.Data.Healthy);
        Assert.Equal(1, result.Data.Missing);
        Assert.Single(result.Data.Problems);
    }

    [Fact]
    public async Task GetFilesByTagAsync_ReturnsPagedFiles()
    {
        var service = CreateStorageService();
        await CreateStoredFileAsync("photo-1.txt", "1"u8.ToArray(), "photo,summer");
        await CreateStoredFileAsync("photo-2.txt", "2"u8.ToArray(), "photo,winter");
        await CreateStoredFileAsync("doc.txt", "3"u8.ToArray(), "document");

        var result = await service.GetFilesByTagAsync("photo");

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.Items.Count);
        Assert.All(result.Data.Items, item => Assert.Contains("photo", item.GetTagsList(), StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BatchVerifyIntegrityAsync_CursorWalksTheWholeStore_NotJustTheOldestFiles()
    {
        // 每次都取「最老的前 N 条」，第 N+1 条之后的文件永远轮不到。
        var service = CreateStorageService();
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
            ids.Add((await CreateStoredFileAsync($"f{i}.txt", [(byte)i])).Id);

        var seen = 0;
        var calls = 0;
        Guid? cursor = null;
        do
        {
            var batch = await service.BatchVerifyIntegrityAsync(maxFiles: 2, after: cursor);
            Assert.True(batch.Succeeded, batch.Message);
            seen += batch.Data!.TotalChecked;
            cursor = batch.Data.NextCursor;
            calls++;
        }
        while (cursor != null && calls < 10);

        Assert.Null(cursor);
        Assert.Equal(3, calls);
        Assert.Equal(5, seen);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(StorageQueryLimits.MaxIntegrityBatch + 1)]
    public async Task BatchVerifyIntegrityAsync_BatchSizeOutOfRange_Is400(int maxFiles)
    {
        // 「0 = 全部」在请求路径上等于一次把全表读出来并逐个哈希。
        var service = CreateStorageService();

        var result = await service.BatchVerifyIntegrityAsync(maxFiles);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
    }

    [Fact]
    public async Task GetFilesByTagAsync_MatchesWholeTagsInTheDatabase_SoTotalAndPageAgree()
    {
        // 先按子串分页、再在内存里精确过滤：total 把 "invoice" 也算进 "voice" 里，页面却被滤短。
        var service = CreateStorageService();
        await CreateStoredFileAsync("a.txt", "a"u8.ToArray(), "invoice,2024");
        await CreateStoredFileAsync("b.txt", "b"u8.ToArray(), "voice");
        await CreateStoredFileAsync("c.txt", "c"u8.ToArray(), "memo,Voice");

        var result = await service.GetFilesByTagAsync("voice", pageIndex: 1, pageSize: 10);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.TotalCount);
        Assert.Equal(2, result.Data.Items.Count);
        Assert.DoesNotContain(result.Data.Items, f => f.OriginalName == "a.txt");
    }

    [Fact]
    public async Task GetFilesByTagAsync_NormalizesPageIndexAndCapsPageSize()
    {
        var service = CreateStorageService();
        await CreateStoredFileAsync("a.txt", "a"u8.ToArray(), "photo");

        var negative = await service.GetFilesByTagAsync("photo", pageIndex: -3, pageSize: 20);
        var huge = await service.GetFilesByTagAsync("photo", pageIndex: 1, pageSize: 1_000_000);

        Assert.True(negative.Succeeded, negative.Message);
        Assert.Equal(1, negative.Data!.PageIndex);
        Assert.Single(negative.Data.Items);
        Assert.Equal(StorageQueryLimits.MaxPageSize, huge.Data!.PageSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(StorageQueryLimits.MaxTopUsers + 1)]
    public async Task GetTopUsersByStorageAsync_TopOutOfRange_Is400(int top)
    {
        var service = CreateStorageService();

        var result = await service.GetTopUsersByStorageAsync(top);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
    }
}
