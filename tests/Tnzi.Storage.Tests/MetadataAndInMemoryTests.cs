using Mapster;
using MapsterMapper;
using Tnzi.Mapster;

namespace Tnzi.Storage.Tests;

/// <summary>
/// Tests for E1-1 (FileRecord Metadata JSON), E1-2 (InMemoryFileStorage), and P1 (batch delete references)
/// </summary>
public class MetadataAndInMemoryTests
{
    private readonly Mock<IRepository<FileRecord, Guid>> _mockFileRepository;
    private readonly Mock<IRepository<FileReference, Guid>> _mockReferenceRepository;
    private readonly Mock<IFileStorage> _mockStorage;
    private readonly StorageOptions _options;
    private readonly Mock<IServiceProvider> _mockServiceProvider;

    public MetadataAndInMemoryTests()
    {
        _mockFileRepository = new Mock<IRepository<FileRecord, Guid>>();
        _mockReferenceRepository = new Mock<IRepository<FileReference, Guid>>();
        _mockStorage = new Mock<IFileStorage>();
        _mockServiceProvider = new Mock<IServiceProvider>();

        // Setup Mapster for MapTo calls
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(ILoggerFactory)))
            .Returns(loggerFactory.Object);

        _options = new StorageOptions
        {
            MaxFileSize = 10 * 1024 * 1024,
            AllowedExtensions = new[] { ".jpg", ".png", ".pdf", ".txt" },
            AutoGenerateThumbnail = false
        };
    }

    private FileStorageService CreateStorageService()
    {
        var optionsMonitor = new Mock<IOptionsMonitor<StorageOptions>>();
        optionsMonitor.Setup(x => x.CurrentValue).Returns(_options);
        return new FileStorageService(
            _mockFileRepository.Object,
            _mockReferenceRepository.Object,
            _mockStorage.Object,
            optionsMonitor.Object,
            TestFileAccessAuthorizer.AllowAll(),
            TestPublicFileFieldResolver.Empty(),
            new TestFileUrlSigner(),
            _mockServiceProvider.Object);
    }

    #region E1-1: FileRecord Metadata JSON

    [Fact]
    public void FileRecord_GetMetadata_ReturnsEmptyDict_WhenNull()
    {
        var record = new FileRecord { Metadata = null };
        var metadata = record.GetMetadata();
        Assert.NotNull(metadata);
        Assert.Empty(metadata);
    }

    [Fact]
    public void FileRecord_GetMetadata_ReturnsEmptyDict_WhenEmpty()
    {
        var record = new FileRecord { Metadata = "" };
        var metadata = record.GetMetadata();
        Assert.NotNull(metadata);
        Assert.Empty(metadata);
    }

    [Fact]
    public void FileRecord_GetMetadata_ReturnsParsedDict()
    {
        var record = new FileRecord { Metadata = "{\"author\":\"John\",\"department\":\"HR\"}" };
        var metadata = record.GetMetadata();
        Assert.Equal(2, metadata.Count);
        Assert.Equal("John", metadata["author"]);
        Assert.Equal("HR", metadata["department"]);
    }

    [Fact]
    public void FileRecord_GetMetadata_ReturnsEmptyDict_WhenInvalidJson()
    {
        var record = new FileRecord { Metadata = "not valid json" };
        var metadata = record.GetMetadata();
        Assert.NotNull(metadata);
        Assert.Empty(metadata);
    }

    [Fact]
    public void FileRecord_SetMetadata_SetsJsonString()
    {
        var record = new FileRecord();
        record.SetMetadata(new Dictionary<string, string> { { "key1", "val1" }, { "key2", "val2" } });

        Assert.NotNull(record.Metadata);
        Assert.Contains("\"key1\":\"val1\"", record.Metadata);
        Assert.Contains("\"key2\":\"val2\"", record.Metadata);
    }

    [Fact]
    public void FileRecord_SetMetadata_SetsNull_WhenEmpty()
    {
        var record = new FileRecord { Metadata = "{\"old\":\"data\"}" };
        record.SetMetadata(new Dictionary<string, string>());
        Assert.Null(record.Metadata);
    }

    [Fact]
    public void FileRecord_SetMetadata_SetsNull_WhenNull()
    {
        var record = new FileRecord { Metadata = "{\"old\":\"data\"}" };
        record.SetMetadata(null);
        Assert.Null(record.Metadata);
    }

    [Fact]
    public async Task SetMetadataAsync_UpdatesFileRecord()
    {
        // Arrange
        var service = CreateStorageService();
        var fileId = Guid.NewGuid();
        var record = new FileRecord { Id = fileId, OriginalName = "test.jpg" };

        _mockFileRepository.Setup(r => r.GetAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var metadata = new Dictionary<string, string> { { "author", "Alice" }, { "project", "Demo" } };

        // Act
        var result = await service.SetMetadataAsync(fileId, metadata);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        // Verify entity state was updated
        Assert.NotNull(record.Metadata);
        Assert.Contains("\"author\":\"Alice\"", record.Metadata);
        _mockFileRepository.Verify(r => r.UpdateAsync(It.IsAny<FileRecord>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetMetadataAsync_ClearsMetadata_WhenEmptyDict()
    {
        // Arrange
        var service = CreateStorageService();
        var fileId = Guid.NewGuid();
        var record = new FileRecord { Id = fileId, OriginalName = "test.jpg", Metadata = "{\"old\":\"data\"}" };

        _mockFileRepository.Setup(r => r.GetAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        // Act
        var result = await service.SetMetadataAsync(fileId, new Dictionary<string, string>());

        // Assert
        Assert.True(result.Succeeded);
        // Verify entity state was cleared
        Assert.Null(record.Metadata);
    }

    [Fact]
    public async Task SetMetadataAsync_ReturnsNotFound_WhenFileDoesNotExist()
    {
        // Arrange
        var service = CreateStorageService();
        _mockFileRepository.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileRecord?)null);

        // Act
        var result = await service.SetMetadataAsync(Guid.NewGuid(), new Dictionary<string, string> { { "k", "v" } });

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }

    [Fact]
    public async Task GetMetadataAsync_ReturnsDictionary()
    {
        // Arrange
        var service = CreateStorageService();
        var fileId = Guid.NewGuid();
        var record = new FileRecord
        {
            Id = fileId,
            OriginalName = "test.jpg",
            Metadata = "{\"camera\":\"Canon\",\"location\":\"NYC\"}"
        };

        _mockFileRepository.Setup(r => r.GetAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        // Act
        var result = await service.GetMetadataAsync(fileId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.Count);
        Assert.Equal("Canon", result.Data["camera"]);
    }

    [Fact]
    public async Task GetMetadataAsync_ReturnsEmptyDict_WhenNoMetadata()
    {
        // Arrange
        var service = CreateStorageService();
        var fileId = Guid.NewGuid();
        var record = new FileRecord { Id = fileId, OriginalName = "test.jpg", Metadata = null };

        _mockFileRepository.Setup(r => r.GetAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        // Act
        var result = await service.GetMetadataAsync(fileId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task GetMetadataAsync_ReturnsNotFound_WhenFileDoesNotExist()
    {
        // Arrange
        var service = CreateStorageService();
        _mockFileRepository.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileRecord?)null);

        // Act
        var result = await service.GetMetadataAsync(Guid.NewGuid());

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }

    #endregion

    #region E1-2: InMemoryFileStorage

    [Fact]
    public async Task InMemory_UploadAndDownload_RoundTrip()
    {
        // Arrange
        var storage = new InMemoryFileStorage();
        var content = "Hello, world!"u8.ToArray();

        // Act
        var path = await storage.UploadAsync("test.txt", new MemoryStream(content), "text/plain");
        using var downloaded = await storage.DownloadAsync(path);
        using var ms = new MemoryStream();
        await downloaded.CopyToAsync(ms);

        // Assert
        Assert.NotNull(path);
        Assert.Equal(content, ms.ToArray());
    }

    [Fact]
    public async Task InMemory_ExistsAsync_ReturnsTrueAfterUpload()
    {
        var storage = new InMemoryFileStorage();
        var path = await storage.UploadAsync("file.txt", new MemoryStream(new byte[] { 1, 2, 3 }));

        Assert.True(await storage.ExistsAsync(path));
        Assert.False(await storage.ExistsAsync("nonexistent.txt"));
    }

    [Fact]
    public async Task InMemory_DeleteAsync_RemovesFile()
    {
        var storage = new InMemoryFileStorage();
        var path = await storage.UploadAsync("file.txt", new MemoryStream(new byte[] { 1 }));

        Assert.True(await storage.DeleteAsync(path));
        Assert.False(await storage.ExistsAsync(path));
        Assert.Equal(0, storage.FileCount);
    }

    [Fact]
    public async Task InMemory_DeleteAsync_ReturnsFalse_WhenNotExists()
    {
        var storage = new InMemoryFileStorage();
        Assert.False(await storage.DeleteAsync("nonexistent.txt"));
    }

    [Fact]
    public async Task InMemory_GetFileSizeAsync_ReturnsCorrectSize()
    {
        var storage = new InMemoryFileStorage();
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var path = await storage.UploadAsync("file.bin", new MemoryStream(data));

        Assert.Equal(5L, await storage.GetFileSizeAsync(path));
    }

    [Fact]
    public async Task InMemory_GetFileSizeAsync_ReturnsZero_WhenNotExists()
    {
        var storage = new InMemoryFileStorage();
        Assert.Equal(0L, await storage.GetFileSizeAsync("nonexistent.txt"));
    }

    [Fact]
    public async Task InMemory_DownloadRangeAsync_ReturnsPartialContent()
    {
        var storage = new InMemoryFileStorage();
        var data = new byte[] { 10, 20, 30, 40, 50 };
        var path = await storage.UploadAsync("file.bin", new MemoryStream(data));

        // Act: get bytes 1-3
        var (stream, start, end, totalLength) = await storage.DownloadRangeAsync(path, 1, 3);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);

        // Assert
        Assert.Equal(1L, start);
        Assert.Equal(3L, end);
        Assert.Equal(5L, totalLength);
        Assert.Equal(new byte[] { 20, 30, 40 }, ms.ToArray());
    }

    [Fact]
    public async Task InMemory_DownloadRangeAsync_ReturnsFullContent_WhenNoRange()
    {
        var storage = new InMemoryFileStorage();
        var data = new byte[] { 1, 2, 3 };
        var path = await storage.UploadAsync("file.bin", new MemoryStream(data));

        var (stream, start, end, totalLength) = await storage.DownloadRangeAsync(path);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);

        Assert.Equal(0L, start);
        Assert.Equal(2L, end);
        Assert.Equal(3L, totalLength);
        Assert.Equal(data, ms.ToArray());
    }

    [Fact]
    public async Task InMemory_DownloadAsync_Throws_WhenNotExists()
    {
        var storage = new InMemoryFileStorage();
        await Assert.ThrowsAsync<Tnzi.Storage.Exceptions.FileNotFoundException>(
            () => storage.DownloadAsync("missing.txt"));
    }

    [Fact]
    public async Task InMemory_ProviderName_IsInMemory()
    {
        var storage = new InMemoryFileStorage();
        Assert.Equal("InMemory", storage.ProviderName);
    }

    [Fact]
    public async Task InMemory_Clear_RemovesAllFiles()
    {
        var storage = new InMemoryFileStorage();
        await storage.UploadAsync("a.txt", new MemoryStream(new byte[] { 1 }));
        await storage.UploadAsync("b.txt", new MemoryStream(new byte[] { 2 }));
        Assert.Equal(2, storage.FileCount);

        storage.Clear();
        Assert.Equal(0, storage.FileCount);
    }

    [Fact]
    public void InMemory_IsRegistered_InProviderFactory()
    {
        Assert.True(StorageProviderFactory.IsRegistered("InMemory"));
        Assert.True(StorageProviderFactory.IsRegistered("inmemory"));
    }

    #endregion

    #region P1: Batch Delete References

    [Fact]
    public async Task DeleteAsync_UsesBatchDelete_ForReferences()
    {
        // Arrange
        var service = CreateStorageService();
        var fileId = Guid.NewGuid();
        var record = new FileRecord
        {
            Id = fileId,
            OriginalName = "test.jpg",
            Path = "path/to/test.jpg",
            ReferenceCount = 0 // Will trigger deletion
        };

        _mockFileRepository.Setup(r => r.GetAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        _mockStorage.Setup(s => s.DeleteAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        // Act
        var result = await service.DeleteAsync(fileId);

        // Assert
        Assert.True(result.Succeeded);
        // Verify batch delete was called with predicate (not individual deletes)
        _mockReferenceRepository.Verify(
            r => r.DeleteAsync(It.IsAny<System.Linq.Expressions.Expression<Func<FileReference, bool>>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        // Verify individual DeleteAsync was NOT called
        _mockReferenceRepository.Verify(
            r => r.DeleteAsync(It.IsAny<FileReference>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion
}
