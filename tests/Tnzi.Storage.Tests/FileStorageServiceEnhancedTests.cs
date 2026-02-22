
namespace Tnzi.Storage.Tests;

/// <summary>
/// FileStorageService 增强功能单元测试
/// 测试新增的查询、引用管理、文件操作等功能
/// </summary>
public class FileStorageServiceEnhancedTests
{
    private readonly Mock<IRepository<FileRecord, Guid>> _mockFileRepository;
    private readonly Mock<IRepository<FileReference, Guid>> _mockReferenceRepository;
    private readonly Mock<IRepository<Entities.FileShare, Guid>> _mockShareRepository;
    private readonly Mock<IFileStorage> _mockStorage;
    private readonly StorageOptions _options;
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<ILoggerFactory> _mockLoggerFactory;

    public FileStorageServiceEnhancedTests()
    {
        _mockFileRepository = new Mock<IRepository<FileRecord, Guid>>();
        _mockReferenceRepository = new Mock<IRepository<FileReference, Guid>>();
        _mockShareRepository = new Mock<IRepository<Entities.FileShare, Guid>>();
        _mockStorage = new Mock<IFileStorage>();
        _options = new StorageOptions();
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockLoggerFactory = new Mock<ILoggerFactory>();

        // 设置 ILoggerFactory（ApplicationService.Logger 需要）
        _mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(ILoggerFactory)))
            .Returns(_mockLoggerFactory.Object);
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

    #region 文件查询功能测试
    // 注意：QueryFilesAsync 测试需要集成测试，因为 Moq 无法 Mock 扩展方法 AsQueryable
    // 这些测试应该在集成测试中使用真实的 DbContext 进行测试
    #endregion

    #region 引用查询功能测试
    // 注意：引用查询测试需要集成测试，因为 Moq 无法 Mock 扩展方法 AsQueryable
    // 这些测试应该在集成测试中使用真实的 DbContext 进行测试
    #endregion

    #region 引用计数同步功能测试
    // 注意：引用计数同步测试需要集成测试，因为 Moq 无法 Mock 扩展方法 AsQueryable
    // 这些测试应该在集成测试中使用真实的 DbContext 进行测试
    #endregion

    #region 文件复制功能测试

    [Fact]
    public async Task CopyAsync_CreatesNewFileRecord_WithNewPath()
    {
        // Arrange
        var service = CreateStorageService();
        var sourceFileId = Guid.NewGuid();
        var sourceFile = new FileRecord
        {
            Id = sourceFileId,
            FileName = "original.jpg",
            OriginalName = "original.jpg",
            Extension = ".jpg",
            Size = 1000,
            Path = "path/to/file.jpg",
            Md5Hash = "abc123",
            Provider = "Local",
            ContentType = "image/jpeg",
            ReferenceCount = 1
        };

        _mockFileRepository.Setup(r => r.GetAsync(sourceFileId, It.IsAny<CancellationToken>())).ReturnsAsync(sourceFile);
        _mockFileRepository.Setup(r => r.InsertAsync(It.IsAny<FileRecord>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        // CopyAsync 现在下载源文件并重新上传
        // 使用 Returns 工厂方法，每次调用返回新的 MemoryStream
        _mockStorage.Setup(s => s.DownloadAsync(It.IsAny<string>()))
            .Returns(() => Task.FromResult<Stream>(new MemoryStream(new byte[] { 1, 2, 3 })));
        _mockStorage.Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync("path/to/copy.jpg");

        // Act
        var result = await service.CopyAsync(sourceFileId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("path/to/copy.jpg", result.Data.Path);
        Assert.Equal(sourceFile.Extension, result.Data.Extension);
        Assert.Equal(0, result.Data.ReferenceCount);
        _mockFileRepository.Verify(r => r.InsertAsync(It.IsAny<FileRecord>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CopyAsync_ThrowsException_WhenSourceFileNotFound()
    {
        // Arrange
        var service = CreateStorageService();
        var sourceFileId = Guid.NewGuid();

        _mockFileRepository.Setup(r => r.GetAsync(sourceFileId, It.IsAny<CancellationToken>())).ReturnsAsync((FileRecord?)null);

        // Act
        var result = await service.CopyAsync(sourceFileId);

        // Assert
        Assert.False(result.Succeeded);
    }

    #endregion

    #region 文件分享功能测试（FileShareService）

    [Fact]
    public async Task CreateShareAsync_CreatesShare_WithGeneratedToken()
    {
        // Arrange
        var service = CreateShareService();
        var fileId = Guid.NewGuid();
        var fileRecord = new FileRecord { Id = fileId, FileName = "test.jpg" };

        _mockFileRepository.Setup(r => r.GetAsync(fileId, It.IsAny<CancellationToken>())).ReturnsAsync(fileRecord);
        _mockShareRepository.Setup(r => r.InsertAsync(It.IsAny<Entities.FileShare>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        // Act
        var result = await service.CreateShareAsync(fileId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(fileId, result.Data.FileId);
        Assert.False(string.IsNullOrEmpty(result.Data.ShareToken));
        Assert.True(result.Data.IsEnabled);
        _mockShareRepository.Verify(r => r.InsertAsync(It.IsAny<Entities.FileShare>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ValidateShareAccessAsync_ReturnsTrue_WhenShareIsValid()
    {
        // Arrange
        var service = CreateShareService();
        var shareToken = "test-token";
        var share = new Entities.FileShare
        {
            ShareToken = shareToken,
            IsEnabled = true,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            MaxAccessCount = 10,
            AccessCount = 5,
            RequirePassword = false
        };

        _mockShareRepository.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<Entities.FileShare, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        // Act
        var result = await service.ValidateShareAccessAsync(shareToken);

        // Assert
        Assert.True(result.Succeeded);
        Assert.True(result.Data);
    }

    [Fact]
    public async Task ValidateShareAccessAsync_ReturnsFalse_WhenShareIsExpired()
    {
        // Arrange
        var service = CreateShareService();
        var shareToken = "test-token";
        var share = new Entities.FileShare
        {
            ShareToken = shareToken,
            IsEnabled = true,
            ExpiresAt = DateTime.UtcNow.AddDays(-1), // 已过期
            RequirePassword = false
        };

        _mockShareRepository.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<Entities.FileShare, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        // Act
        var result = await service.ValidateShareAccessAsync(shareToken);

        // Assert
        Assert.True(result.Succeeded);
        Assert.False(result.Data);
    }

    #endregion

    #region 文件压缩功能测试

    [Fact]
    public async Task CompressAsync_CreatesZipFile()
    {
        // Arrange
        var service = CreateStorageService();
        var fileIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var files = fileIds.Select(id => new FileRecord
        {
            Id = id,
            FileName = $"test{id}.jpg",
            OriginalName = $"test{id}.jpg",
            Path = $"path/to/{id}.jpg",
            Size = 1000
        }).ToList();

        _mockFileRepository.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, CancellationToken>((id, ct) => Task.FromResult(files.FirstOrDefault(f => f.Id == id)));

        // 每次调用返回新的 MemoryStream（避免 using 处置后复用同一实例）
        _mockStorage.Setup(s => s.DownloadAsync(It.IsAny<string>()))
            .Returns(() => Task.FromResult<Stream>(new MemoryStream(new byte[] { 1, 2, 3 })));

        _mockStorage.Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync("path/to/archive.zip");

        _mockFileRepository.Setup(r => r.InsertAsync(It.IsAny<FileRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await service.CompressAsync(fileIds);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(".zip", result.Data.Extension);
        Assert.Equal("application/zip", result.Data.ContentType);
        _mockFileRepository.Verify(r => r.InsertAsync(It.IsAny<FileRecord>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CompressAsync_ThrowsException_WhenNoFilesProvided()
    {
        // Arrange
        var service = CreateStorageService();

        // Act
        var result = await service.CompressAsync(Array.Empty<Guid>());

        // Assert
        Assert.False(result.Succeeded);
    }

    #endregion
}
