namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// Tests for T11 (query contract: ContentType filter + SortOrder string + preview URL route),
/// T12 (delete physical consistency), and T13 (folder move atomicity + server-side copy fallback).
/// </summary>
public class StorageQueryDeleteCopyTests : StorageIntegrationTestBase
{
    // ==================== T11: ContentType filter ====================

    [Fact]
    public async Task QueryFilesAsync_ContentTypePrefix_ReturnsOnlyImages()
    {
        var service = CreateStorageService();
        await SeedFileAsync("a.png", "image/png");
        await SeedFileAsync("b.jpg", "image/jpeg");
        await SeedFileAsync("c.pdf", "application/pdf");
        await SeedFileAsync("d.txt", "text/plain");
        DbContext.ChangeTracker.Clear();

        var result = await service.QueryFilesAsync(new FileQueryRequest { ContentType = "image/" });

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.TotalCount);
        Assert.All(result.Data.Items, f => Assert.StartsWith("image/", f.ContentType));
    }

    [Fact]
    public async Task QueryFilesAsync_ContentTypeExact_ReturnsOnlyMatching()
    {
        var service = CreateStorageService();
        await SeedFileAsync("a.png", "image/png");
        await SeedFileAsync("c.pdf", "application/pdf");
        DbContext.ChangeTracker.Clear();

        var result = await service.QueryFilesAsync(new FileQueryRequest { ContentType = "application/pdf" });

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Data!.TotalCount);
        Assert.Equal("application/pdf", result.Data.Items.Single().ContentType);
    }

    // ==================== T11: SortOrder string ====================

    [Fact]
    public async Task QueryFilesAsync_SortOrderAsc_SortsAscending()
    {
        var service = CreateStorageService();
        await SeedFileAsync("small.txt", "text/plain", size: 10);
        await SeedFileAsync("medium.txt", "text/plain", size: 50);
        await SeedFileAsync("large.txt", "text/plain", size: 100);
        DbContext.ChangeTracker.Clear();

        var result = await service.QueryFilesAsync(new FileQueryRequest { SortBy = "size", SortOrder = "asc" });

        Assert.True(result.Succeeded);
        var sizes = result.Data!.Items.Select(f => f.Size).ToList();
        Assert.Equal(new long[] { 10, 50, 100 }, sizes);
    }

    [Fact]
    public async Task QueryFilesAsync_SortOrderDesc_SortsDescending()
    {
        var service = CreateStorageService();
        await SeedFileAsync("small.txt", "text/plain", size: 10);
        await SeedFileAsync("medium.txt", "text/plain", size: 50);
        await SeedFileAsync("large.txt", "text/plain", size: 100);
        DbContext.ChangeTracker.Clear();

        var result = await service.QueryFilesAsync(new FileQueryRequest { SortBy = "size", SortOrder = "desc" });

        Assert.True(result.Succeeded);
        var sizes = result.Data!.Items.Select(f => f.Size).ToList();
        Assert.Equal(new long[] { 100, 50, 10 }, sizes);
    }

    [Fact]
    public async Task QueryFilesAsync_SortOrderTakesPrecedenceOverDescending()
    {
        var service = CreateStorageService();
        await SeedFileAsync("small.txt", "text/plain", size: 10);
        await SeedFileAsync("large.txt", "text/plain", size: 100);
        DbContext.ChangeTracker.Clear();

        // Descending=true but SortOrder="asc" → ascending wins
        var result = await service.QueryFilesAsync(
            new FileQueryRequest { SortBy = "size", Descending = true, SortOrder = "asc" });

        Assert.True(result.Succeeded);
        var sizes = result.Data!.Items.Select(f => f.Size).ToList();
        Assert.Equal(new long[] { 10, 100 }, sizes);
    }

    [Fact]
    public async Task QueryFilesAsync_NoSortOrder_FallsBackToDescendingBool()
    {
        var service = CreateStorageService();
        await SeedFileAsync("small.txt", "text/plain", size: 10);
        await SeedFileAsync("large.txt", "text/plain", size: 100);
        DbContext.ChangeTracker.Clear();

        var result = await service.QueryFilesAsync(
            new FileQueryRequest { SortBy = "size", Descending = true });

        Assert.True(result.Succeeded);
        var sizes = result.Data!.Items.Select(f => f.Size).ToList();
        Assert.Equal(new long[] { 100, 10 }, sizes);
    }

    // ==================== T11: Preview URL route ====================

    [Fact]
    public async Task GetPreviewUrlAsync_NonImage_UsesPluralFilesRoute()
    {
        var preview = new FilePreviewService(Storage, ServiceProvider);
        var record = new FileRecord
        {
            Id = Guid.NewGuid(),
            FileName = "doc.pdf",
            OriginalName = "doc.pdf",
            Extension = ".pdf",
            Path = "2026/06/20/doc.pdf",
            ContentType = "application/pdf"
        };

        var url = await preview.GetPreviewUrlAsync(record);

        // Must point at the real registered route: /api/files/preview/{id}/preview (plural "files").
        Assert.DoesNotContain("/api/file/preview", url);
        Assert.StartsWith($"/api/files/preview/{record.Id}/preview", url);
        Assert.Contains("type=pdf", url);
    }

    // ==================== T12: Delete physical consistency ====================

    [Fact]
    public async Task DeleteAsync_PhysicalDeleteFails_KeepsDbRecord()
    {
        var fake = new DeleteFailingFileStorage();
        var path = await fake.UploadAsync("keep.txt", new MemoryStream("data"u8.ToArray()), "text/plain");
        var service = CreateStorageService(fake);

        var record = new FileRecord
        {
            Id = Guid.NewGuid(),
            FileName = "keep.txt",
            OriginalName = "keep.txt",
            Extension = ".txt",
            Size = 4,
            Path = path,
            Provider = fake.ProviderName,
            ContentType = "text/plain",
            ReferenceCount = 1
        };
        DbContext.FileRecords.Add(record);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        // Reference count drops to 0, then physical delete is attempted and FAILS.
        var result = await service.DeleteAsync(record.Id);

        Assert.True(result.Succeeded);
        // DB record must be retained for background cleanup retry.
        DbContext.ChangeTracker.Clear();
        Assert.Contains(DbContext.FileRecords, r => r.Id == record.Id);
    }

    [Fact]
    public async Task DeleteAsync_PhysicalDeleteSucceeds_RemovesDbRecord()
    {
        var service = CreateStorageService();
        var file = await CreateStoredFileAsync("delete-me.txt", "data"u8.ToArray());
        DbContext.ChangeTracker.Clear();

        var result = await service.DeleteAsync(file.Id);

        Assert.True(result.Succeeded);
        DbContext.ChangeTracker.Clear();
        Assert.DoesNotContain(DbContext.FileRecords, r => r.Id == file.Id);
        Assert.False(await Storage.ExistsAsync(file.Path!));
    }

    [Fact]
    public async Task DeleteAsync_FileAlreadyGone_RemovesDbRecord()
    {
        // Physical file missing → DeleteAsync returns false but ExistsAsync also false → safe to delete record.
        var service = CreateStorageService();
        var record = new FileRecord
        {
            Id = Guid.NewGuid(),
            FileName = "ghost.txt",
            OriginalName = "ghost.txt",
            Extension = ".txt",
            Size = 1,
            Path = "missing/ghost.txt",
            Provider = Storage.ProviderName,
            ContentType = "text/plain",
            ReferenceCount = 1
        };
        DbContext.FileRecords.Add(record);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var result = await service.DeleteAsync(record.Id);

        Assert.True(result.Succeeded);
        DbContext.ChangeTracker.Clear();
        Assert.DoesNotContain(DbContext.FileRecords, r => r.Id == record.Id);
    }

    // ==================== T13: Folder move atomicity ====================

    [Fact]
    public async Task MoveAsync_UpdatesDescendantPathsConsistently()
    {
        var service = CreateFolderService();
        var parent = (await service.CreateAsync(new CreateFileFolderDto { Name = "parent" })).Data!;
        var child = (await service.CreateAsync(new CreateFileFolderDto { Name = "child", ParentId = parent.Id })).Data!;
        var grandchild = (await service.CreateAsync(new CreateFileFolderDto { Name = "grandchild", ParentId = child.Id })).Data!;
        var newRoot = (await service.CreateAsync(new CreateFileFolderDto { Name = "newroot" })).Data!;
        DbContext.ChangeTracker.Clear();

        var result = await service.MoveAsync(parent.Id, newRoot.Id);

        Assert.True(result.Succeeded);
        DbContext.ChangeTracker.Clear();
        Assert.Equal("/newroot/parent", DbContext.FileFolders.Single(f => f.Id == parent.Id).Path);
        Assert.Equal("/newroot/parent/child", DbContext.FileFolders.Single(f => f.Id == child.Id).Path);
        Assert.Equal("/newroot/parent/child/grandchild", DbContext.FileFolders.Single(f => f.Id == grandchild.Id).Path);
    }

    [Fact]
    public async Task UpdateAsync_Rename_UpdatesDescendantPathsConsistently()
    {
        var service = CreateFolderService();
        var parent = (await service.CreateAsync(new CreateFileFolderDto { Name = "docs" })).Data!;
        var child = (await service.CreateAsync(new CreateFileFolderDto { Name = "sub", ParentId = parent.Id })).Data!;
        DbContext.ChangeTracker.Clear();

        var result = await service.UpdateAsync(parent.Id, new UpdateFileFolderDto { Name = "documents" });

        Assert.True(result.Succeeded);
        DbContext.ChangeTracker.Clear();
        Assert.Equal("/documents", DbContext.FileFolders.Single(f => f.Id == parent.Id).Path);
        Assert.Equal("/documents/sub", DbContext.FileFolders.Single(f => f.Id == child.Id).Path);
    }

    // ==================== T13: Copy server-side fallback ====================

    [Fact]
    public async Task CopyAsync_LocalProvider_FallsBackToDownloadUpload_ProducesCorrectContent()
    {
        var service = CreateStorageService();
        var source = await CreateStoredFileAsync("orig.txt", "copy-me-content"u8.ToArray());
        DbContext.ChangeTracker.Clear();

        var result = await service.CopyAsync(source.Id, "copy.txt");

        Assert.True(result.Succeeded);
        var copy = result.Data!;
        Assert.NotEqual(source.Path, copy.Path);
        using var downloaded = await Storage.DownloadAsync(copy.Path!);
        using var reader = new StreamReader(downloaded);
        Assert.Equal("copy-me-content", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task IFileStorage_DefaultCopyAsync_ReturnsNull()
    {
        // Local provider does not override CopyAsync, so the interface default returns null,
        // signalling the service to fall back to download+upload.
        IFileStorage storage = Storage;
        var result = await storage.CopyAsync("any/source.txt", "dest.txt");
        Assert.Null(result);
    }

    // ==================== Helpers ====================

    private async Task SeedFileAsync(string name, string contentType, long size = 4)
    {
        using var stream = new MemoryStream(new byte[size]);
        var path = await Storage.UploadAsync(name, stream, contentType);
        DbContext.FileRecords.Add(new FileRecord
        {
            Id = Guid.NewGuid(),
            FileName = name,
            OriginalName = name,
            Extension = Path.GetExtension(name),
            Size = size,
            Path = path,
            Provider = Storage.ProviderName,
            ContentType = contentType,
            ReferenceCount = 1
        });
        await DbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Fake storage whose DeleteAsync always reports failure while the file genuinely still exists,
    /// to exercise the "keep DB record when physical delete fails" path (T12).
    /// </summary>
    private sealed class DeleteFailingFileStorage : IFileStorage
    {
        private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);

        public string ProviderName => "DeleteFailing";

        public Task<string> UploadAsync(string fileName, Stream stream, string? contentType = null)
        {
            var path = $"fake/{fileName}";
            _files.Add(path);
            return Task.FromResult(path);
        }

        public Task<Stream> DownloadAsync(string filePath) => Task.FromResult<Stream>(new MemoryStream());

        // Always report failure even though the file is still present.
        public Task<bool> DeleteAsync(string filePath) => Task.FromResult(false);

        public Task<bool> ExistsAsync(string filePath) => Task.FromResult(_files.Contains(filePath));

        public Task<string> GetUrlAsync(string filePath, int? expiresIn = null) => Task.FromResult(filePath);

        public Task<long> GetFileSizeAsync(string filePath) => Task.FromResult(0L);

        public Task<(Stream Stream, long Start, long End, long TotalLength)> DownloadRangeAsync(
            string filePath, long? rangeStart = null, long? rangeEnd = null)
            => Task.FromResult<(Stream, long, long, long)>((new MemoryStream(), 0L, 0L, 0L));
    }
}
