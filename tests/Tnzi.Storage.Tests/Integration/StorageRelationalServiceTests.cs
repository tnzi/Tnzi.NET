namespace Tnzi.Storage.Tests.Integration;

public class StorageRelationalServiceTests : StorageIntegrationTestBase
{
    [Fact]
    public async Task CreateVersionAsync_CreatesNewVersion()
    {
        var service = CreateVersionService();
        var file = await CreateStoredFileAsync("versioned.txt", "v1"u8.ToArray());

        var result = await service.CreateVersionAsync(file.Id, new MemoryStream("v2"u8.ToArray()), "Second version");

        Assert.True(result.Succeeded);
        Assert.Equal(2, DbContext.FileVersions.Count());
        Assert.True(DbContext.FileVersions.Single(v => v.Version == 2).IsCurrent);
    }

    [Fact]
    public async Task UploadChunkAsync_UploadsChunk()
    {
        var service = CreateChunkUploadService();
        var session = (await service.InitiateChunkedUploadAsync("chunked.txt", 6, 3)).Data!;

        var result = await service.UploadChunkAsync(session.Id, 0, new MemoryStream("abc"u8.ToArray()));

        Assert.True(result.Succeeded);
        Assert.Single(DbContext.FileChunks);
        Assert.Equal(1, DbContext.FileUploadSessions.Single().UploadedChunks);
        Assert.Equal(3, DbContext.FileUploadSessions.Single().UploadedSize);
    }

    [Fact]
    public async Task CompleteChunkedUploadAsync_MergesChunks()
    {
        var service = CreateChunkUploadService();
        var session = (await service.InitiateChunkedUploadAsync("merged.txt", 6, 3)).Data!;
        await service.UploadChunkAsync(session.Id, 0, new MemoryStream("abc"u8.ToArray()));
        await service.UploadChunkAsync(session.Id, 1, new MemoryStream("def"u8.ToArray()));

        var result = await service.CompleteChunkedUploadAsync(session.Id);

        Assert.True(result.Succeeded);
        Assert.Single(DbContext.FileRecords);
        using var downloaded = await Storage.DownloadAsync(result.Data!.Path!);
        using var reader = new StreamReader(downloaded);
        Assert.Equal("abcdef", await reader.ReadToEndAsync());
        Assert.Empty(DbContext.FileChunks);
    }

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
    public async Task GetSharesByFileAsync_ReturnsShareSummaries()
    {
        var service = CreateShareService();
        var file = await CreateStoredFileAsync("shared.txt", "content"u8.ToArray());
        DbContext.FileShares.AddRange(
            new Tnzi.Storage.Entities.FileShare { Id = Guid.NewGuid(), FileId = file.Id, ShareToken = "older", IsEnabled = true, CreationTime = DateTime.UtcNow.AddMinutes(-5) },
            new Tnzi.Storage.Entities.FileShare { Id = Guid.NewGuid(), FileId = file.Id, ShareToken = "newer", IsEnabled = true, CreationTime = DateTime.UtcNow });
        await DbContext.SaveChangesAsync();

        var result = await service.GetSharesByFileAsync(file.Id);

        Assert.True(result.Succeeded);
        var shares = result.Data!.ToList();
        Assert.Equal(2, shares.Count);
        Assert.Equal("newer", shares[0].ShareToken);
        Assert.All(shares, item => Assert.Equal(file.OriginalName, item.OriginalName));
    }

    [Fact]
    public async Task GetActiveSharesAsync_ReturnsPaged()
    {
        var service = CreateShareService();
        var file = await CreateStoredFileAsync("active.txt", "content"u8.ToArray());
        DbContext.FileShares.AddRange(
            new Tnzi.Storage.Entities.FileShare { Id = Guid.NewGuid(), FileId = file.Id, ShareToken = "enabled-1", IsEnabled = true, CreationTime = DateTime.UtcNow.AddMinutes(-2) },
            new Tnzi.Storage.Entities.FileShare { Id = Guid.NewGuid(), FileId = file.Id, ShareToken = "enabled-2", IsEnabled = true, CreationTime = DateTime.UtcNow.AddMinutes(-1) },
            new Tnzi.Storage.Entities.FileShare { Id = Guid.NewGuid(), FileId = file.Id, ShareToken = "disabled", IsEnabled = false, CreationTime = DateTime.UtcNow });
        await DbContext.SaveChangesAsync();

        var result = await service.GetActiveSharesAsync(new ActiveSharesQueryRequest { PageIndex = 1, PageSize = 10 });

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.TotalCount);
        Assert.DoesNotContain(result.Data.Items, x => x.ShareToken == "disabled");
    }

    [Fact]
    public async Task BatchRevokeSharesAsync_RevokesMultiple()
    {
        var service = CreateShareService();
        var file = await CreateStoredFileAsync("revoke.txt", "content"u8.ToArray());
        var share1 = new Tnzi.Storage.Entities.FileShare { Id = Guid.NewGuid(), FileId = file.Id, ShareToken = "s1", IsEnabled = true };
        var share2 = new Tnzi.Storage.Entities.FileShare { Id = Guid.NewGuid(), FileId = file.Id, ShareToken = "s2", IsEnabled = true };
        DbContext.FileShares.AddRange(share1, share2);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var result = await service.BatchRevokeSharesAsync([share1.Id, share2.Id]);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data);
        Assert.All(DbContext.FileShares, share => Assert.False(share.IsEnabled));
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
