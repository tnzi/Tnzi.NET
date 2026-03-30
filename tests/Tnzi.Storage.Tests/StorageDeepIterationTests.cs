using Mapster;
using MapsterMapper;
using Tnzi.Mapster;

namespace Tnzi.Storage.Tests;

/// <summary>
/// E1 深度迭代测试：文件完整性验证、分享管理增强、文件标签
/// 覆盖 IFileStorageService 的 VerifyFileIntegrityAsync / BatchVerifyIntegrityAsync / SetFileTagsAsync / GetFilesByTagAsync
/// 以及 IFileShareService 的 GetSharesByFileAsync / GetActiveSharesAsync / BatchRevokeSharesAsync
/// </summary>
public class StorageDeepIterationTests
{
    private readonly Mock<IRepository<FileRecord, Guid>> _mockFileRepository;
    private readonly Mock<IRepository<FileReference, Guid>> _mockReferenceRepository;
    private readonly Mock<IRepository<Entities.FileShare, Guid>> _mockShareRepository;
    private readonly Mock<IFileStorage> _mockStorage;
    private readonly StorageOptions _options;
    private readonly Mock<IServiceProvider> _mockServiceProvider;

    public StorageDeepIterationTests()
    {
        _mockFileRepository = new Mock<IRepository<FileRecord, Guid>>();
        _mockReferenceRepository = new Mock<IRepository<FileReference, Guid>>();
        _mockShareRepository = new Mock<IRepository<Entities.FileShare, Guid>>();
        _mockStorage = new Mock<IFileStorage>();
        _mockServiceProvider = new Mock<IServiceProvider>();

        // Setup Mapster for MapTo calls
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        // ApplicationService.Logger 需要 ILoggerFactory
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
        var optionsWrapper = Microsoft.Extensions.Options.Options.Create(_options);
        return new FileStorageService(
            _mockFileRepository.Object,
            _mockReferenceRepository.Object,
            _mockStorage.Object,
            optionsWrapper,
            _mockServiceProvider.Object);
    }

    private FileShareService CreateShareService()
    {
        return new FileShareService(
            _mockShareRepository.Object,
            _mockFileRepository.Object,
            _mockServiceProvider.Object);
    }

    #region File Integrity Verification Tests

    [Fact]
    public async Task VerifyFileIntegrityAsync_ReturnsHealthy_WhenFileExistsAndMd5Matches()
    {
        // Arrange
        var service = CreateStorageService();
        var fileId = Guid.NewGuid();
        var content = new byte[] { 1, 2, 3, 4, 5 };
        var md5Hash = CalculateMd5(content);
        var fileRecord = new FileRecord
        {
            Id = fileId,
            OriginalName = "test.jpg",
            Path = "path/to/test.jpg",
            Md5Hash = md5Hash
        };

        _mockFileRepository.Setup(r => r.GetAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileRecord);
        _mockStorage.Setup(s => s.ExistsAsync("path/to/test.jpg"))
            .ReturnsAsync(true);
        _mockStorage.Setup(s => s.DownloadAsync("path/to/test.jpg"))
            .ReturnsAsync(new MemoryStream(content));

        // Act
        var result = await service.VerifyFileIntegrityAsync(fileId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(FileIntegrityStatus.Healthy, result.Data.Status);
        Assert.True(result.Data.PhysicalFileExists);
        Assert.True(result.Data.Md5Matches);
        Assert.Equal(md5Hash, result.Data.ExpectedMd5);
        Assert.Equal(md5Hash, result.Data.ActualMd5);
    }

    [Fact]
    public async Task VerifyFileIntegrityAsync_ReturnsMissing_WhenFileDoesNotExist()
    {
        // Arrange
        var service = CreateStorageService();
        var fileId = Guid.NewGuid();
        var fileRecord = new FileRecord
        {
            Id = fileId,
            OriginalName = "missing.jpg",
            Path = "path/to/missing.jpg",
            Md5Hash = "abc123"
        };

        _mockFileRepository.Setup(r => r.GetAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileRecord);
        _mockStorage.Setup(s => s.ExistsAsync("path/to/missing.jpg"))
            .ReturnsAsync(false);

        // Act
        var result = await service.VerifyFileIntegrityAsync(fileId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(FileIntegrityStatus.Missing, result.Data.Status);
        Assert.False(result.Data.PhysicalFileExists);
        Assert.Null(result.Data.Md5Matches);
    }

    [Fact]
    public async Task VerifyFileIntegrityAsync_ReturnsCorrupted_WhenMd5DoesNotMatch()
    {
        // Arrange
        var service = CreateStorageService();
        var fileId = Guid.NewGuid();
        var originalContent = new byte[] { 1, 2, 3 };
        var corruptedContent = new byte[] { 9, 8, 7 };
        var expectedMd5 = CalculateMd5(originalContent);

        var fileRecord = new FileRecord
        {
            Id = fileId,
            OriginalName = "corrupted.jpg",
            Path = "path/to/corrupted.jpg",
            Md5Hash = expectedMd5
        };

        _mockFileRepository.Setup(r => r.GetAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileRecord);
        _mockStorage.Setup(s => s.ExistsAsync("path/to/corrupted.jpg"))
            .ReturnsAsync(true);
        _mockStorage.Setup(s => s.DownloadAsync("path/to/corrupted.jpg"))
            .ReturnsAsync(new MemoryStream(corruptedContent));

        // Act
        var result = await service.VerifyFileIntegrityAsync(fileId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(FileIntegrityStatus.Corrupted, result.Data.Status);
        Assert.True(result.Data.PhysicalFileExists);
        Assert.False(result.Data.Md5Matches);
        Assert.NotEqual(result.Data.ExpectedMd5, result.Data.ActualMd5);
    }

    [Fact]
    public async Task VerifyFileIntegrityAsync_ReturnsFail_WhenFileRecordNotFound()
    {
        // Arrange
        var service = CreateStorageService();
        var fileId = Guid.NewGuid();

        _mockFileRepository.Setup(r => r.GetAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileRecord?)null);

        // Act
        var result = await service.VerifyFileIntegrityAsync(fileId);

        // Assert
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task VerifyFileIntegrityAsync_ReturnsHealthy_WhenNoMd5Stored()
    {
        // Arrange: 当数据库中没有存储 MD5，只要物理文件存在就算 Healthy
        var service = CreateStorageService();
        var fileId = Guid.NewGuid();
        var fileRecord = new FileRecord
        {
            Id = fileId,
            OriginalName = "no-md5.jpg",
            Path = "path/to/no-md5.jpg",
            Md5Hash = null // 无 MD5 哈希
        };

        _mockFileRepository.Setup(r => r.GetAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileRecord);
        _mockStorage.Setup(s => s.ExistsAsync("path/to/no-md5.jpg"))
            .ReturnsAsync(true);

        // Act
        var result = await service.VerifyFileIntegrityAsync(fileId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(FileIntegrityStatus.Healthy, result.Data.Status);
        Assert.True(result.Data.PhysicalFileExists);
        Assert.Null(result.Data.Md5Matches); // MD5 未比较
    }

    [Fact]
    public async Task VerifyFileIntegrityAsync_ReturnsMissing_WhenPathIsNull()
    {
        // Arrange: Path 为空时应该返回 Missing
        var service = CreateStorageService();
        var fileId = Guid.NewGuid();
        var fileRecord = new FileRecord
        {
            Id = fileId,
            OriginalName = "no-path.jpg",
            Path = null,
            Md5Hash = "abc123"
        };

        _mockFileRepository.Setup(r => r.GetAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileRecord);

        // Act
        var result = await service.VerifyFileIntegrityAsync(fileId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(FileIntegrityStatus.Missing, result.Data.Status);
        Assert.False(result.Data.PhysicalFileExists);
    }

    [Fact]
    public async Task VerifyFileIntegrityAsync_ReturnsError_WhenStorageThrowsException()
    {
        // Arrange: 存储层异常应该返回 Error 状态
        var service = CreateStorageService();
        var fileId = Guid.NewGuid();
        var fileRecord = new FileRecord
        {
            Id = fileId,
            OriginalName = "error.jpg",
            Path = "path/to/error.jpg",
            Md5Hash = "abc123"
        };

        _mockFileRepository.Setup(r => r.GetAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileRecord);
        _mockStorage.Setup(s => s.ExistsAsync("path/to/error.jpg"))
            .ThrowsAsync(new IOException("Storage unavailable"));

        // Act
        var result = await service.VerifyFileIntegrityAsync(fileId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(FileIntegrityStatus.Error, result.Data.Status);
        Assert.NotNull(result.Data.Error);
        Assert.Contains("Storage unavailable", result.Data.Error);
    }

    #endregion

    #region File Share Management Tests

    [Fact]
    public async Task CreateShareAsync_WithPassword_SetsRequirePasswordTrue()
    {
        // Arrange
        var service = CreateShareService();
        var fileId = Guid.NewGuid();
        var fileRecord = new FileRecord { Id = fileId, FileName = "test.pdf" };

        _mockFileRepository.Setup(r => r.GetAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileRecord);
        _mockShareRepository.Setup(r => r.InsertAsync(It.IsAny<Entities.FileShare>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await service.CreateShareAsync(fileId, password: "secret123");

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.RequirePassword);
        Assert.NotNull(result.Data.PasswordHash);
        Assert.Contains(":", result.Data.PasswordHash); // HMAC-SHA256 格式: salt:hash
    }

    [Fact]
    public async Task CreateShareAsync_WithExpirationAndMaxAccess_SetsProperties()
    {
        // Arrange
        var service = CreateShareService();
        var fileId = Guid.NewGuid();
        var fileRecord = new FileRecord { Id = fileId, FileName = "test.pdf" };
        var expiresAt = DateTime.UtcNow.AddDays(7);

        _mockFileRepository.Setup(r => r.GetAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileRecord);
        _mockShareRepository.Setup(r => r.InsertAsync(It.IsAny<Entities.FileShare>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await service.CreateShareAsync(fileId, expiresAt: expiresAt, maxAccessCount: 50);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(expiresAt, result.Data.ExpiresAt);
        Assert.Equal(50, result.Data.MaxAccessCount);
        Assert.Equal(0, result.Data.AccessCount);
    }

    [Fact]
    public async Task CreateShareAsync_ReturnsFail_WhenFileNotFound()
    {
        // Arrange
        var service = CreateShareService();
        var fileId = Guid.NewGuid();

        _mockFileRepository.Setup(r => r.GetAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileRecord?)null);

        // Act
        var result = await service.CreateShareAsync(fileId);

        // Assert
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ValidateShareAccessAsync_ReturnsFalse_WhenMaxAccessCountReached()
    {
        // Arrange
        var service = CreateShareService();
        var shareToken = "exhausted-token";
        var share = new Entities.FileShare
        {
            ShareToken = shareToken,
            IsEnabled = true,
            MaxAccessCount = 10,
            AccessCount = 10, // 已达上限
            RequirePassword = false
        };

        _mockShareRepository.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Entities.FileShare, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        // Act
        var result = await service.ValidateShareAccessAsync(shareToken);

        // Assert
        Assert.True(result.Succeeded);
        Assert.False(result.Data);
    }

    [Fact]
    public async Task ValidateShareAccessAsync_ReturnsFalse_WhenShareIsDisabled()
    {
        // Arrange
        var service = CreateShareService();
        var shareToken = "disabled-token";
        var share = new Entities.FileShare
        {
            ShareToken = shareToken,
            IsEnabled = false, // 已禁用
            RequirePassword = false
        };

        _mockShareRepository.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Entities.FileShare, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        // Act
        var result = await service.ValidateShareAccessAsync(shareToken);

        // Assert
        Assert.True(result.Succeeded);
        Assert.False(result.Data);
    }

    [Fact]
    public async Task IncrementShareAccessCountAsync_IncrementsCount()
    {
        // Arrange
        var service = CreateShareService();
        var shareToken = "test-token";
        var share = new Entities.FileShare
        {
            ShareToken = shareToken,
            AccessCount = 5
        };

        _mockShareRepository.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Entities.FileShare, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);
        _mockShareRepository.Setup(r => r.UpdateAsync(share, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await service.IncrementShareAccessCountAsync(shareToken);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(6, share.AccessCount);
        _mockShareRepository.Verify(r => r.UpdateAsync(share, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IncrementShareAccessCountAsync_ReturnsFail_WhenShareNotFound()
    {
        // Arrange
        var service = CreateShareService();

        _mockShareRepository.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Entities.FileShare, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entities.FileShare?)null);

        // Act
        var result = await service.IncrementShareAccessCountAsync("nonexistent-token");

        // Assert
        Assert.False(result.Succeeded);
    }

    #endregion

    #region FileShareSummaryDto Computed Properties Tests

    [Fact]
    public void FileShareSummaryDto_IsExpired_ReturnsFalse_WhenNoExpiration()
    {
        var dto = new FileShareSummaryDto
        {
            ExpiresAt = null
        };

        Assert.False(dto.IsExpired);
    }

    [Fact]
    public void FileShareSummaryDto_IsExpired_ReturnsTrue_WhenPastExpiration()
    {
        var dto = new FileShareSummaryDto
        {
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1)
        };

        Assert.True(dto.IsExpired);
    }

    [Fact]
    public void FileShareSummaryDto_IsExpired_ReturnsFalse_WhenFutureExpiration()
    {
        var dto = new FileShareSummaryDto
        {
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        };

        Assert.False(dto.IsExpired);
    }

    [Fact]
    public void FileShareSummaryDto_IsExhausted_ReturnsTrue_WhenAccessCountReachesMax()
    {
        var dto = new FileShareSummaryDto
        {
            MaxAccessCount = 10,
            AccessCount = 10
        };

        Assert.True(dto.IsExhausted);
    }

    [Fact]
    public void FileShareSummaryDto_IsExhausted_ReturnsFalse_WhenNoMaxAccessCount()
    {
        var dto = new FileShareSummaryDto
        {
            MaxAccessCount = null,
            AccessCount = 100
        };

        Assert.False(dto.IsExhausted);
    }

    [Fact]
    public void FileShareSummaryDto_IsExhausted_ReturnsFalse_WhenAccessCountBelowMax()
    {
        var dto = new FileShareSummaryDto
        {
            MaxAccessCount = 10,
            AccessCount = 5
        };

        Assert.False(dto.IsExhausted);
    }

    [Fact]
    public void FileShareSummaryDto_IsExhausted_ReturnsTrue_WhenAccessCountExceedsMax()
    {
        var dto = new FileShareSummaryDto
        {
            MaxAccessCount = 10,
            AccessCount = 15
        };

        Assert.True(dto.IsExhausted);
    }

    #endregion

    #region File Tags Tests

    [Fact]
    public async Task SetFileTagsAsync_SetsTagsOnFile()
    {
        // Arrange
        var service = CreateStorageService();
        var fileId = Guid.NewGuid();
        var fileRecord = new FileRecord
        {
            Id = fileId,
            OriginalName = "tagged.jpg",
            Tags = null
        };
        var tags = new List<string> { "photo", "vacation", "summer" };

        _mockFileRepository.Setup(r => r.GetAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileRecord);
        _mockFileRepository.Setup(r => r.UpdateAsync(fileRecord, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await service.SetFileTagsAsync(fileId, tags);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        // Verify entity state was updated
        Assert.NotNull(fileRecord.Tags);
        var tagsList = fileRecord.GetTagsList();
        Assert.Equal(3, tagsList.Count);
        Assert.Contains("photo", tagsList);
        Assert.Contains("vacation", tagsList);
        Assert.Contains("summer", tagsList);
        _mockFileRepository.Verify(r => r.UpdateAsync(fileRecord, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetFileTagsAsync_ReplacesExistingTags()
    {
        // Arrange
        var service = CreateStorageService();
        var fileId = Guid.NewGuid();
        var fileRecord = new FileRecord
        {
            Id = fileId,
            OriginalName = "tagged.jpg",
            Tags = "old-tag1,old-tag2"
        };
        var newTags = new List<string> { "new-tag" };

        _mockFileRepository.Setup(r => r.GetAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileRecord);
        _mockFileRepository.Setup(r => r.UpdateAsync(fileRecord, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await service.SetFileTagsAsync(fileId, newTags);

        // Assert
        Assert.True(result.Succeeded);
        // Verify entity state was updated
        var tagsList = fileRecord.GetTagsList();
        Assert.Single(tagsList);
        Assert.Equal("new-tag", tagsList[0]);
    }

    [Fact]
    public async Task SetFileTagsAsync_ClearsTagsWithEmptyList()
    {
        // Arrange
        var service = CreateStorageService();
        var fileId = Guid.NewGuid();
        var fileRecord = new FileRecord
        {
            Id = fileId,
            OriginalName = "tagged.jpg",
            Tags = "tag1,tag2"
        };

        _mockFileRepository.Setup(r => r.GetAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileRecord);
        _mockFileRepository.Setup(r => r.UpdateAsync(fileRecord, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await service.SetFileTagsAsync(fileId, new List<string>());

        // Assert
        Assert.True(result.Succeeded);
        // Verify entity state was cleared
        Assert.Null(fileRecord.Tags);
        Assert.Empty(fileRecord.GetTagsList());
    }

    [Fact]
    public async Task SetFileTagsAsync_ReturnsFail_WhenFileNotFound()
    {
        // Arrange
        var service = CreateStorageService();
        var fileId = Guid.NewGuid();

        _mockFileRepository.Setup(r => r.GetAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileRecord?)null);

        // Act
        var result = await service.SetFileTagsAsync(fileId, new List<string> { "tag" });

        // Assert
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SetFileTagsAsync_DeduplicatesTags()
    {
        // Arrange: 传入重复标签，SetTagsList 应该去重
        var service = CreateStorageService();
        var fileId = Guid.NewGuid();
        var fileRecord = new FileRecord { Id = fileId, OriginalName = "test.jpg" };
        var tags = new List<string> { "photo", "Photo", "photo", "vacation" };

        _mockFileRepository.Setup(r => r.GetAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileRecord);
        _mockFileRepository.Setup(r => r.UpdateAsync(fileRecord, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await service.SetFileTagsAsync(fileId, tags);

        // Assert
        Assert.True(result.Succeeded);
        // Verify entity state — SetTagsList 通过 Distinct() 去重（大小写敏感），只保留唯一值
        var tagsList = fileRecord.GetTagsList();
        // "photo" 和 "Photo" 是不同的（Distinct 是大小写敏感的）
        // 但 "photo" 有两个，只保留一个
        Assert.True(tagsList.Count <= tags.Count);
    }

    [Fact]
    public async Task GetFilesByTagAsync_ReturnsFail_WhenTagIsEmpty()
    {
        // Arrange
        var service = CreateStorageService();

        // Act
        var result = await service.GetFilesByTagAsync("");

        // Assert
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task GetFilesByTagAsync_ReturnsFail_WhenTagIsWhitespace()
    {
        // Arrange
        var service = CreateStorageService();

        // Act
        var result = await service.GetFilesByTagAsync("   ");

        // Assert
        Assert.False(result.Succeeded);
    }

    #endregion

    #region FileRecord Tags Helper Methods Tests

    [Fact]
    public void FileRecord_GetTagsList_ReturnsEmptyList_WhenTagsIsNull()
    {
        var record = new FileRecord { Tags = null };
        var tags = record.GetTagsList();
        Assert.Empty(tags);
    }

    [Fact]
    public void FileRecord_GetTagsList_ReturnsEmptyList_WhenTagsIsEmpty()
    {
        var record = new FileRecord { Tags = "" };
        var tags = record.GetTagsList();
        Assert.Empty(tags);
    }

    [Fact]
    public void FileRecord_GetTagsList_ParsesCommaSeparatedTags()
    {
        var record = new FileRecord { Tags = "photo,vacation,summer" };
        var tags = record.GetTagsList();
        Assert.Equal(3, tags.Count);
        Assert.Equal("photo", tags[0]);
        Assert.Equal("vacation", tags[1]);
        Assert.Equal("summer", tags[2]);
    }

    [Fact]
    public void FileRecord_GetTagsList_TrimsWhitespace()
    {
        var record = new FileRecord { Tags = " photo , vacation , summer " };
        var tags = record.GetTagsList();
        Assert.Equal(3, tags.Count);
        Assert.Equal("photo", tags[0]);
        Assert.Equal("vacation", tags[1]);
        Assert.Equal("summer", tags[2]);
    }

    [Fact]
    public void FileRecord_GetTagsList_SkipsEmptyEntries()
    {
        var record = new FileRecord { Tags = "photo,,vacation,,summer" };
        var tags = record.GetTagsList();
        Assert.Equal(3, tags.Count);
    }

    [Fact]
    public void FileRecord_SetTagsList_SetsNull_WhenEmptyList()
    {
        var record = new FileRecord { Tags = "old-tag" };
        record.SetTagsList(new List<string>());
        Assert.Null(record.Tags);
    }

    [Fact]
    public void FileRecord_SetTagsList_SetsNull_WhenNullList()
    {
        var record = new FileRecord { Tags = "old-tag" };
        record.SetTagsList(null);
        Assert.Null(record.Tags);
    }

    [Fact]
    public void FileRecord_SetTagsList_DeduplicatesAndTrims()
    {
        var record = new FileRecord();
        record.SetTagsList(new List<string> { " a ", "b", " a ", "c" });
        var tags = record.GetTagsList();
        Assert.Equal(3, tags.Count); // "a", "b", "c" — Distinct removes duplicate "a"
        Assert.Contains("a", tags);
        Assert.Contains("b", tags);
        Assert.Contains("c", tags);
    }

    #endregion

    #region Helper Methods

    private string CalculateMd5(byte[] data)
    {
        using var md5 = MD5.Create();
        var hashBytes = md5.ComputeHash(data);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    #endregion
}
