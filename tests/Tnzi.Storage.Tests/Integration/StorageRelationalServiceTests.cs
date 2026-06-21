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

    // ==================== T9: Version content download / delete ====================

    [Fact]
    public async Task GetVersionContentAsync_ReturnsBytesOfRequestedVersion()
    {
        var service = CreateVersionService();
        var file = await CreateStoredFileAsync("versioned.txt", "v1-content"u8.ToArray());

        // CreateVersion 会把当前文件快照成 v1，并把新上传内容存为 v2
        var v2 = await service.CreateVersionAsync(file.Id, new MemoryStream("v2-content"u8.ToArray()), "Second version");
        Assert.True(v2.Succeeded);

        // 取 v1（原始快照内容）
        var v1Result = await service.GetVersionContentAsync(file.Id, 1);
        Assert.True(v1Result.Succeeded);
        Assert.Equal("v1-content", await ReadAllText(v1Result.Data!));

        // 取 v2（新上传内容）
        var v2Result = await service.GetVersionContentAsync(file.Id, 2);
        Assert.True(v2Result.Succeeded);
        Assert.Equal("v2-content", await ReadAllText(v2Result.Data!));
    }

    [Fact]
    public async Task GetVersionContentAsync_ReturnsNotFound_WhenVersionMissing()
    {
        var service = CreateVersionService();
        var file = await CreateStoredFileAsync("versioned.txt", "v1"u8.ToArray());

        var result = await service.GetVersionContentAsync(file.Id, 99);

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }

    [Fact]
    public async Task GetVersionContentAsync_DoesNotChangeCurrentVersionPointer()
    {
        var service = CreateVersionService();
        var file = await CreateStoredFileAsync("versioned.txt", "v1"u8.ToArray());
        await service.CreateVersionAsync(file.Id, new MemoryStream("v2"u8.ToArray()));

        // 只读获取历史版本不应改变 IsCurrent 指针（v2 仍为当前）
        var read = await service.GetVersionContentAsync(file.Id, 1);
        Assert.True(read.Succeeded);
        await read.Data!.DisposeAsync();

        DbContext.ChangeTracker.Clear();
        var current = DbContext.FileVersions.Single(v => v.IsCurrent);
        Assert.Equal(2, current.Version);
    }

    [Fact]
    public async Task DeleteVersionAsync_RemovesRecordAndPhysicalFile()
    {
        var service = CreateVersionService();
        var file = await CreateStoredFileAsync("versioned.txt", "v1"u8.ToArray());
        await service.CreateVersionAsync(file.Id, new MemoryStream("v2"u8.ToArray()));

        DbContext.ChangeTracker.Clear();
        var v1Path = DbContext.FileVersions.AsNoTracking().Single(v => v.Version == 1).Path;
        Assert.True(await Storage.ExistsAsync(v1Path));

        var result = await service.DeleteVersionAsync(file.Id, 1);

        Assert.True(result.Succeeded);
        DbContext.ChangeTracker.Clear();
        Assert.DoesNotContain(DbContext.FileVersions, v => v.Version == 1);
        Assert.False(await Storage.ExistsAsync(v1Path));
    }

    [Fact]
    public async Task DeleteVersionAsync_RejectsDeletingCurrentVersion()
    {
        var service = CreateVersionService();
        var file = await CreateStoredFileAsync("versioned.txt", "v1"u8.ToArray());
        await service.CreateVersionAsync(file.Id, new MemoryStream("v2"u8.ToArray()));

        // v2 是当前版本，禁止删除
        var result = await service.DeleteVersionAsync(file.Id, 2);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        DbContext.ChangeTracker.Clear();
        Assert.Contains(DbContext.FileVersions, v => v.Version == 2);
    }

    [Fact]
    public async Task DeleteVersionAsync_ReturnsNotFound_WhenVersionMissing()
    {
        var service = CreateVersionService();
        var file = await CreateStoredFileAsync("versioned.txt", "v1"u8.ToArray());

        var result = await service.DeleteVersionAsync(file.Id, 99);

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }

    // ==================== T10: Atomic share access count + no PasswordHash leak ====================

    [Fact]
    public async Task IncrementShareAccessCountAsync_IsAtomicAndRespectsMaxAccessCount()
    {
        var service = CreateShareService();
        var file = await CreateStoredFileAsync("limited.txt", "content"u8.ToArray());
        var share = new Tnzi.Storage.Entities.FileShare
        {
            Id = Guid.NewGuid(),
            FileId = file.Id,
            ShareToken = "limited-token",
            IsEnabled = true,
            MaxAccessCount = 1,
            AccessCount = 0
        };
        DbContext.FileShares.Add(share);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        // 第一次：成功占额
        var first = await service.IncrementShareAccessCountAsync("limited-token");
        Assert.True(first.Succeeded);
        Assert.True(first.Data);

        // 第二次：已达上限，返回 false（不再自增）
        var second = await service.IncrementShareAccessCountAsync("limited-token");
        Assert.True(second.Succeeded);
        Assert.False(second.Data);

        // AccessCount 最终为 1，未被突破
        DbContext.ChangeTracker.Clear();
        var persisted = DbContext.FileShares.Single(s => s.ShareToken == "limited-token");
        Assert.Equal(1, persisted.AccessCount);
    }

    [Fact]
    public async Task IncrementShareAccessCountAsync_ReturnsFalse_WhenDisabledOrMissing()
    {
        var service = CreateShareService();
        var file = await CreateStoredFileAsync("disabled.txt", "content"u8.ToArray());
        DbContext.FileShares.Add(new Tnzi.Storage.Entities.FileShare
        {
            Id = Guid.NewGuid(),
            FileId = file.Id,
            ShareToken = "disabled-token",
            IsEnabled = false
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var disabled = await service.IncrementShareAccessCountAsync("disabled-token");
        Assert.True(disabled.Succeeded);
        Assert.False(disabled.Data);

        var missing = await service.IncrementShareAccessCountAsync("does-not-exist");
        Assert.True(missing.Succeeded);
        Assert.False(missing.Data);
    }

    [Fact]
    public async Task IncrementShareAccessCountAsync_UnlimitedShare_AlwaysSucceeds()
    {
        var service = CreateShareService();
        var file = await CreateStoredFileAsync("unlimited.txt", "content"u8.ToArray());
        DbContext.FileShares.Add(new Tnzi.Storage.Entities.FileShare
        {
            Id = Guid.NewGuid(),
            FileId = file.Id,
            ShareToken = "unlimited-token",
            IsEnabled = true,
            MaxAccessCount = null,
            AccessCount = 0
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        Assert.True((await service.IncrementShareAccessCountAsync("unlimited-token")).Data);
        Assert.True((await service.IncrementShareAccessCountAsync("unlimited-token")).Data);

        DbContext.ChangeTracker.Clear();
        var persisted = DbContext.FileShares.Single(s => s.ShareToken == "unlimited-token");
        Assert.Equal(2, persisted.AccessCount);
    }

    [Fact]
    public void FileSharePublicDto_HasNoPasswordHashField()
    {
        // 公开 DTO 绝不携带 PasswordHash（信息泄漏防护）
        var prop = typeof(FileSharePublicDto).GetProperty("PasswordHash");
        Assert.Null(prop);
    }

    private static async Task<string> ReadAllText(Stream stream)
    {
        using (stream)
        using (var reader = new StreamReader(stream))
        {
            return await reader.ReadToEndAsync();
        }
    }
}
