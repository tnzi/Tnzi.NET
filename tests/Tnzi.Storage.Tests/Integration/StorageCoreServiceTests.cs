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
}
