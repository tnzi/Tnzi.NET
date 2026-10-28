
namespace Tnzi.Storage.Tests;

/// <summary>
/// FileStorageService 与 FileReferenceService 的全面单元测试。
/// 分享 / 版本 / 分片上传三组随实现搬到了 Tnzi.Storage.Workspace.Tests。
/// </summary>
public class FileStorageServiceComprehensiveTests
{
    private readonly Mock<IRepository<FileRecord, Guid>> _mockFileRepository;
    private readonly Mock<IRepository<FileReference, Guid>> _mockReferenceRepository;
    private readonly Mock<IFileStorage> _mockStorage;
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly StorageOptions _options;

    public FileStorageServiceComprehensiveTests()
    {
        _mockFileRepository = new Mock<IRepository<FileRecord, Guid>>();
        _mockReferenceRepository = new Mock<IRepository<FileReference, Guid>>();
        _mockStorage = new Mock<IFileStorage>();
        _mockServiceProvider = new Mock<IServiceProvider>();

        // 设置 ILoggerFactory（ApplicationService.Logger 需要）
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(ILoggerFactory)))
            .Returns(loggerFactory.Object);

        // 会话归属判定要问「当前用户是谁」——这份夹具此前从不注册它，
        // 于是上传会话的四个端点从来没有在「有人在操作」的语境下被测过。
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(u => u.Id).Returns(TestHelper.DefaultTestUserId);
        currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(ICurrentUser)))
            .Returns(currentUser.Object);

        _options = new StorageOptions
        {
            MaxFileSize = 10 * 1024 * 1024, // 10MB
            AllowedExtensions = new[] { ".jpg", ".png", ".pdf", ".txt" },
            AutoGenerateThumbnail = false // 测试时禁用缩略图生成
        };

        // MD5 去重的候选查询走 AsQueryable（要排序、要异步）；默认一条记录都没有。
        GivenStoredRecords();

        // 删除前要数一数正式引用行（只剩引用行持有的文件不让删）；默认一行都没有。
        _mockReferenceRepository.Setup(r => r.AsQueryable(It.IsAny<bool>()))
            .Returns(() => new List<FileReference>().BuildMock());
    }

    /// <summary>
    /// 仓储里「已有」的文件记录：去重查询从它们里挑，按 id 回取时给的也是同一个实例。
    /// </summary>
    private void GivenStoredRecords(params FileRecord[] records)
    {
        _mockFileRepository.Setup(r => r.AsQueryable(It.IsAny<bool>()))
            .Returns(() => records.ToList().BuildMock());
        foreach (var record in records)
        {
            _mockFileRepository.Setup(r => r.GetAsync(record.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(record);
        }
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
            _mockServiceProvider.Object,
            new UploadGuard(optionsMonitor.Object),
            new FileThumbnailGenerator(_mockStorage.Object, optionsMonitor.Object),
            UnlimitedStorageQuota.Instance);
    }

    private FileReferenceService CreateReferenceService()
    {
        return new FileReferenceService(
            _mockFileRepository.Object,
            _mockReferenceRepository.Object,
            _mockServiceProvider.Object);
    }

    #region 基础文件操作测试

    [Fact]
    public async Task SaveAsync_SavesNewFile_ReturnsFileRecord()
    {
        // Arrange
        var service = CreateStorageService();
        var fileName = "test.jpg";
        var content = new byte[] { 1, 2, 3, 4, 5 };
        var stream = new MemoryStream(content);

        _mockStorage.Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync("path/to/file.jpg");
        _mockFileRepository.Setup(r => r.InsertAsync(It.IsAny<FileRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await service.SaveAsync(fileName, stream);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(fileName, result.Data.OriginalName);
        Assert.Equal(".jpg", result.Data.Extension);
        Assert.Equal(content.Length, result.Data.Size);
        _mockStorage.Verify(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()), Times.Once);
        _mockFileRepository.Verify(r => r.InsertAsync(It.IsAny<FileRecord>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveAsync_ThrowsException_WhenFileSizeExceedsLimit()
    {
        // Arrange
        var service = CreateStorageService();
        var fileName = "large.jpg";
        var largeContent = new byte[_options.MaxFileSize + 1];
        var stream = new MemoryStream(largeContent);

        // Act
        var result = await service.SaveAsync(fileName, stream);

        // Assert
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SaveAsync_ThrowsException_WhenFileTypeNotAllowed()
    {
        // Arrange
        var service = CreateStorageService();
        var fileName = "test.exe";
        var content = new byte[] { 1, 2, 3 };
        var stream = new MemoryStream(content);

        // Act
        var result = await service.SaveAsync(fileName, stream);

        // Assert
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task GetAsync_ReturnsFileStream()
    {
        // Arrange
        var service = CreateStorageService();
        var fileId = Guid.NewGuid();
        var fileRecord = new FileRecord
        {
            Id = fileId,
            Path = "path/to/file.jpg"
        };
        var expectedStream = new MemoryStream(new byte[] { 1, 2, 3 });

        _mockFileRepository.Setup(r => r.GetAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileRecord);
        _mockStorage.Setup(s => s.DownloadAsync("path/to/file.jpg"))
            .ReturnsAsync(expectedStream);

        // Act
        var result = await service.GetAsync(fileId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        _mockStorage.Verify(s => s.DownloadAsync("path/to/file.jpg"), Times.Once);
    }

    [Fact]
    public async Task GetAsync_ThrowsException_WhenFileNotFound()
    {
        // Arrange
        var service = CreateStorageService();
        var fileId = Guid.NewGuid();

        _mockFileRepository.Setup(r => r.GetAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileRecord?)null);

        // Act
        var result = await service.GetAsync(fileId);

        // Assert
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task GetRecordAsync_ReturnsFileRecord()
    {
        // Arrange
        var service = CreateStorageService();
        var fileId = Guid.NewGuid();
        var fileRecord = new FileRecord { Id = fileId };

        _mockFileRepository.Setup(r => r.GetAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileRecord);

        // Act
        var result = await service.GetRecordAsync(fileId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(fileId, result.Data.Id);
    }

    [Fact]
    public async Task GetUrlAsync_ReturnsUrl()
    {
        // Arrange
        var service = CreateStorageService();
        var fileId = Guid.NewGuid();
        var fileRecord = new FileRecord { Id = fileId };

        _mockFileRepository.Setup(r => r.GetAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileRecord);

        // Act
        var result = await service.GetUrlAsync(fileId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal($"/api/files/{fileId}/download", result.Data);
    }

    [Fact]
    public async Task GetOrCreateByMd5Async_ReturnsExistingFile_WhenMd5Exists()
    {
        // Arrange
        var service = CreateStorageService();
        var content = new byte[] { 1, 2, 3 };
        var md5Hash = CalculateMd5(content);
        // 去重只复用调用者自己的记录（见 CrossUserDedupTests）。
        var existingFile = new FileRecord
        {
            Id = Guid.NewGuid(),
            Md5Hash = md5Hash,
            ReferenceCount = 1,
            CreatorId = TestHelper.DefaultTestUserId
        };

        GivenStoredRecords(existingFile);
        _mockFileRepository.Setup(r => r.UpdateAsync(existingFile, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await service.GetOrCreateByMd5Async(md5Hash, "test.jpg", new MemoryStream(content));

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(existingFile.Id, result.Data.Id);
        Assert.Equal(2, existingFile.ReferenceCount);
    }

    [Fact]
    public async Task RenameAsync_RenamesFile()
    {
        // Arrange
        var service = CreateStorageService();
        var fileId = Guid.NewGuid();
        var fileRecord = new FileRecord
        {
            Id = fileId,
            FileName = "old.jpg"
        };

        _mockFileRepository.Setup(r => r.GetAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileRecord);
        _mockFileRepository.Setup(r => r.UpdateAsync(fileRecord, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await service.RenameAsync(fileId, "new.jpg");

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("new.jpg", result.Data.OriginalName);
        _mockFileRepository.Verify(r => r.UpdateAsync(fileRecord, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region 批量操作测试

    [Fact]
    public async Task SaveManyAsync_SavesMultipleFiles()
    {
        // Arrange
        var service = CreateStorageService();
        var files = new List<(string fileName, Stream stream)>
        {
            ("file1.jpg", new MemoryStream(new byte[] { 1, 2, 3 })),
            ("file2.png", new MemoryStream(new byte[] { 4, 5, 6 }))
        };

        _mockStorage.Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync("path/to/file");
        _mockFileRepository.Setup(r => r.InsertAsync(It.IsAny<FileRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var results = await service.SaveManyAsync(files);

        // Assert
        Assert.True(results.Succeeded);
        Assert.NotNull(results.Data);
        Assert.Equal(2, results.Data.Count());
        _mockFileRepository.Verify(r => r.InsertAsync(It.IsAny<FileRecord>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    #endregion

    #region 文件引用管理测试（FileReferenceService）

    [Fact]
    public async Task SaveWithReferenceAsync_SavesFileAndCreatesReference()
    {
        // Arrange
        var service = CreateStorageService();
        var fileName = "test.jpg";
        var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var entityType = "User";
        var entityId = Guid.NewGuid();
        var fieldName = "Avatar";

        _mockStorage.Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync("path/to/file.jpg");
        _mockFileRepository.Setup(r => r.InsertAsync(It.IsAny<FileRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockReferenceRepository.Setup(r => r.InsertAsync(It.IsAny<FileReference>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await service.SaveWithReferenceAsync(fileName, stream, entityType, entityId, fieldName, isTemporary: false);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        _mockReferenceRepository.Verify(r => r.InsertAsync(It.IsAny<FileReference>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmReferenceAsync_ConvertsTemporaryToPermanent()
    {
        // Arrange
        var service = CreateReferenceService();
        var fileId = Guid.NewGuid();
        var entityType = "User";
        var entityId = Guid.NewGuid();
        var fieldName = "Avatar";
        var temporaryRef = new FileReference
        {
            Id = Guid.NewGuid(),
            FileId = fileId,
            EntityType = entityType,
            EntityId = entityId,
            FieldName = fieldName,
            IsTemporary = true
        };

        _mockReferenceRepository.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<FileReference, bool>>>()))
            .ReturnsAsync(temporaryRef);
        _mockReferenceRepository.Setup(r => r.UpdateAsync(temporaryRef, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockFileRepository.Setup(r => r.FindAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileRecord { Id = fileId, ReferenceCount = 0 });
        _mockFileRepository.Setup(r => r.UpdateAsync(It.IsAny<FileRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await service.ConfirmReferenceAsync(fileId, entityType, entityId, fieldName);

        // Assert
        Assert.True(result.Succeeded);
        Assert.False(temporaryRef.IsTemporary);
        _mockReferenceRepository.Verify(r => r.UpdateAsync(temporaryRef, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateReferenceAsync_UpdatesReference()
    {
        // Arrange
        var service = CreateReferenceService();
        var oldFileId = Guid.NewGuid();
        var newFileId = Guid.NewGuid();
        var entityType = "User";
        var entityId = Guid.NewGuid();
        var fieldName = "Avatar";
        var oldRef = new FileReference
        {
            Id = Guid.NewGuid(),
            FileId = oldFileId,
            EntityType = entityType,
            EntityId = entityId,
            FieldName = fieldName
        };
        var oldFile = new FileRecord
        {
            Id = oldFileId,
            ReferenceCount = 1
        };

        // Mock ToListAsync directly since Where is an extension method on IQueryable
        _mockReferenceRepository.Setup(r => r.ToListAsync(It.IsAny<System.Linq.Expressions.Expression<Func<FileReference, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { oldRef }.ToList());
        _mockReferenceRepository.Setup(r => r.DeleteAsync(oldRef, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockFileRepository.Setup(r => r.GetAsync(oldFileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(oldFile);
        _mockFileRepository.Setup(r => r.UpdateAsync(oldFile, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockReferenceRepository.Setup(r => r.InsertAsync(It.IsAny<FileReference>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await service.UpdateReferenceAsync(oldFileId, newFileId, entityType, entityId, fieldName);

        // Assert
        Assert.True(result.Succeeded);
        _mockReferenceRepository.Verify(r => r.DeleteAsync(oldRef, It.IsAny<CancellationToken>()), Times.Once);
        _mockReferenceRepository.Verify(r => r.InsertAsync(It.IsAny<FileReference>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveFromBytesAsync_SavesFileFromBytes()
    {
        // Arrange
        var service = CreateStorageService();
        var fileName = "test.jpg";
        var content = new byte[] { 1, 2, 3, 4, 5 };

        _mockStorage.Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync("path/to/file.jpg");
        _mockFileRepository.Setup(r => r.InsertAsync(It.IsAny<FileRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await service.SaveFromBytesAsync(fileName, content);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        _mockFileRepository.Verify(r => r.InsertAsync(It.IsAny<FileRecord>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveFromPathAsync_SavesFileFromPath()
    {
        // Arrange
        var service = CreateStorageService();
        var tempFile = Path.GetTempFileName();
        try
        {
            var testContent = new byte[] { 1, 2, 3, 4, 5 };
            await File.WriteAllBytesAsync(tempFile, testContent);

            // Create a file with allowed extension for testing
            var testFileWithExt = tempFile + ".txt";
            File.Move(tempFile, testFileWithExt);

            _mockStorage.Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
                .ReturnsAsync("path/to/file");
            _mockFileRepository.Setup(r => r.InsertAsync(It.IsAny<FileRecord>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await service.SaveFromPathAsync(testFileWithExt);

            // Assert
            Assert.True(result.Succeeded);
            Assert.NotNull(result.Data);
        }
        finally
        {
            var testFileWithExt = tempFile + ".txt";
            if (File.Exists(testFileWithExt))
                File.Delete(testFileWithExt);
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    #endregion



    #region 文件压缩测试

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
    }

    [Fact]
    public async Task CompressAsync_ThrowsException_WhenNoFiles()
    {
        // Arrange
        var service = CreateStorageService();

        // Act
        var result = await service.CompressAsync(Array.Empty<Guid>());

        // Assert
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task DecompressAsync_ExtractsFiles()
    {
        // Arrange
        var service = CreateStorageService();
        var fileId = Guid.NewGuid();
        var fileRecord = new FileRecord
        {
            Id = fileId,
            Extension = ".zip",
            Path = "path/to/archive.zip"
        };

        // 创建一个简单的 ZIP 文件
        using var zipStream = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            var entry = archive.CreateEntry("test.txt");
            using var entryStream = entry.Open();
            await entryStream.WriteAsync(new byte[] { 1, 2, 3 }, 0, 3);
        }
        zipStream.Position = 0;

        _mockFileRepository.Setup(r => r.GetAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileRecord);
        _mockStorage.Setup(s => s.DownloadAsync("path/to/archive.zip"))
            .ReturnsAsync(zipStream);
        _mockStorage.Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync("path/to/extracted.txt");
        _mockFileRepository.Setup(r => r.InsertAsync(It.IsAny<FileRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var results = await service.DecompressAsync(fileId);

        // Assert
        Assert.True(results.Succeeded);
        Assert.NotEmpty(results.Data!);
    }

    #endregion


    #region 辅助方法

    private string CalculateMd5(byte[] data)
    {
        using var md5 = MD5.Create();
        var hashBytes = md5.ComputeHash(data);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    #endregion
}
