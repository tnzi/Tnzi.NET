namespace Tnzi.Storage.Workspace.Tests;

/// <summary>
/// 工作区四个服务的单元测试（仓储全为 mock）。
/// </summary>
/// <remarks>
/// 这些用例原先散在父测试项目的 <c>FileStorageServiceComprehensiveTests</c> /
/// <c>FileStorageServiceEnhancedTests</c> / <c>StorageDeepIterationTests</c> /
/// <c>ResumeDownloadUploadTests</c> 四个类里，随被测服务一起搬过来。
/// 内容一字未改，只把它们从「父模块的测试」归位成「本模块的测试」。
/// </remarks>
public class WorkspaceServiceUnitTests
{
    private readonly Mock<IRepository<FileRecord, Guid>> _mockFileRepository;
    private readonly Mock<IRepository<FileShare, Guid>> _mockShareRepository;
    private readonly Mock<IRepository<FileUploadSession, Guid>> _mockUploadSessionRepository;
    private readonly Mock<IRepository<FileChunk, Guid>> _mockChunkRepository;
    private readonly Mock<IFileStorage> _mockStorage;
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly StorageOptions _options;

    public WorkspaceServiceUnitTests()
    {
        _mockFileRepository = new Mock<IRepository<FileRecord, Guid>>();
        _mockShareRepository = new Mock<IRepository<FileShare, Guid>>();
        _mockUploadSessionRepository = new Mock<IRepository<FileUploadSession, Guid>>();
        _mockChunkRepository = new Mock<IRepository<FileChunk, Guid>>();
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
            // .zip 在列，是因为分片上传的用例传的正是一个大 zip。拆分前它们住在另一个
            // 夹具里、那份夹具**根本没设白名单**，合并后不补上这一项就会被扩展名闸门拦下 ——
            // 与被测行为无关的一次夹具冲突。
            AllowedExtensions = [".jpg", ".png", ".pdf", ".txt", ".zip"],
            AutoGenerateThumbnail = false // 测试时禁用缩略图生成
        };
    }

    private UploadGuard Guard() => new(new StaticOptionsMonitor<StorageOptions>(_options));

    private FileShareService CreateShareService()
    {
        return new FileShareService(
            _mockShareRepository.Object,
            _mockFileRepository.Object,
            TestFileAccessAuthorizer.AllowAll(),
            new FileAccessGrantContext(),
            new StaticOptionsMonitor<StorageOptions>(_options),
            _mockServiceProvider.Object);
    }

    private FileChunkUploadService CreateChunkUploadService()
    {
        return new FileChunkUploadService(
            _mockUploadSessionRepository.Object,
            _mockChunkRepository.Object,
            _mockFileRepository.Object,
            _mockStorage.Object,
            new StaticOptionsMonitor<StorageOptions>(_options),
            _mockServiceProvider.Object,
            Guard());
    }

    #region 文件分享测试（FileShareService）

    [Fact]
    public async Task CreateShareAsync_CreatesShareWithToken()
    {
        // Arrange
        var service = CreateShareService();
        var fileId = Guid.NewGuid();
        var fileRecord = new FileRecord { Id = fileId };

        _mockFileRepository.Setup(r => r.GetAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileRecord);
        _mockShareRepository.Setup(r => r.InsertAsync(It.IsAny<FileShare>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await service.CreateShareAsync(fileId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(fileId, result.Data.FileId);
        Assert.False(string.IsNullOrEmpty(result.Data.ShareToken));
        Assert.True(result.Data.IsEnabled);
    }

    [Fact]
    public async Task ValidateShareAccessAsync_ReturnsTrue_WhenShareIsValid()
    {
        // Arrange
        var service = CreateShareService();
        var shareToken = "test-token";
        var share = new FileShare
        {
            ShareToken = shareToken,
            IsEnabled = true,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            RequirePassword = false
        };

        _mockShareRepository.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<FileShare, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        // Act
        var result = await service.ValidateShareAccessAsync(shareToken);

        // Assert
        Assert.True(result.Succeeded);
        Assert.True(result.Data);
    }

    [Fact]
    public async Task ValidateShareAccessAsync_ReturnsFalse_WhenPasswordIncorrect()
    {
        // Arrange
        var service = CreateShareService();
        var shareToken = "test-token";
        var correctPassword = "correct";
        var incorrectPassword = "wrong";
        var share = new FileShare
        {
            ShareToken = shareToken,
            IsEnabled = true,
            RequirePassword = true,
            PasswordHash = ComputePasswordHash(correctPassword)
        };

        _mockShareRepository.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<FileShare, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        // Act
        var result = await service.ValidateShareAccessAsync(shareToken, incorrectPassword);

        // Assert
        Assert.True(result.Succeeded);
        Assert.False(result.Data);
    }

    [Fact]
    public async Task RevokeShareAsync_DisablesShare()
    {
        // Arrange
        var service = CreateShareService();
        var shareToken = "test-token";
        var share = new FileShare
        {
            ShareToken = shareToken,
            IsEnabled = true,
            // 撤销要求与创建同一份权利；这条用例测的是「撤销确实停用了链接」，
            // 所以让当前用户就是创建者。谁不能撤销见 ShareManagementAuthorizationTests。
            CreatorId = TestHelper.DefaultTestUserId
        };

        _mockShareRepository.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<FileShare, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);
        _mockShareRepository.Setup(r => r.UpdateAsync(share, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await service.RevokeShareAsync(shareToken);

        // Assert
        Assert.True(result.Succeeded);
        Assert.False(share.IsEnabled);
        _mockShareRepository.Verify(r => r.UpdateAsync(share, It.IsAny<CancellationToken>()), Times.Once);
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
        _mockShareRepository.Setup(r => r.InsertAsync(It.IsAny<FileShare>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        // Act
        var result = await service.CreateShareAsync(fileId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(fileId, result.Data.FileId);
        Assert.False(string.IsNullOrEmpty(result.Data.ShareToken));
        Assert.True(result.Data.IsEnabled);
        _mockShareRepository.Verify(r => r.InsertAsync(It.IsAny<FileShare>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ValidateShareAccessAsync_ReturnsTrue_WhenShareIsValid_NoExpiry()
    {
        // Arrange
        var service = CreateShareService();
        var shareToken = "test-token";
        var share = new FileShare
        {
            ShareToken = shareToken,
            IsEnabled = true,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            MaxAccessCount = 10,
            AccessCount = 5,
            RequirePassword = false
        };

        _mockShareRepository.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<FileShare, bool>>>(), It.IsAny<CancellationToken>()))
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
        var share = new FileShare
        {
            ShareToken = shareToken,
            IsEnabled = true,
            ExpiresAt = DateTime.UtcNow.AddDays(-1), // 已过期
            RequirePassword = false
        };

        _mockShareRepository.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<FileShare, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        // Act
        var result = await service.ValidateShareAccessAsync(shareToken);

        // Assert
        Assert.True(result.Succeeded);
        Assert.False(result.Data);
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
        FileShare? inserted = null;
        _mockShareRepository.Setup(r => r.InsertAsync(It.IsAny<FileShare>(), It.IsAny<CancellationToken>()))
            .Callback<FileShare, CancellationToken>((entity, _) => inserted = entity)
            .Returns(Task.CompletedTask);

        // Act
        var result = await service.CreateShareAsync(fileId, password: "secret123");

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.RequirePassword);

        // 口令哈希断言落在**落库的实体**上：对外 DTO 刻意不带 PasswordHash，
        // 而这条用例要证明的是「确实算了一枚 PBKDF2 哈希存进去」（格式见 SharePasswordHasher）。
        Assert.NotNull(inserted);
        Assert.NotNull(inserted!.PasswordHash);
        Assert.StartsWith("pbkdf2$", inserted.PasswordHash);
        Assert.True(SharePasswordHasher.Verify("secret123", inserted.PasswordHash, out var rehash));
        Assert.False(rehash);
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
        _mockShareRepository.Setup(r => r.InsertAsync(It.IsAny<FileShare>(), It.IsAny<CancellationToken>()))
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
        var share = new FileShare
        {
            ShareToken = shareToken,
            IsEnabled = true,
            MaxAccessCount = 10,
            AccessCount = 10, // 已达上限
            RequirePassword = false
        };

        _mockShareRepository.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FileShare, bool>>>(),
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
        var share = new FileShare
        {
            ShareToken = shareToken,
            IsEnabled = false, // 已禁用
            RequirePassword = false
        };

        _mockShareRepository.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FileShare, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        // Act
        var result = await service.ValidateShareAccessAsync(shareToken);

        // Assert
        Assert.True(result.Succeeded);
        Assert.False(result.Data);
    }

    // IncrementShareAccessCountAsync is now an atomic DB-side ExecuteUpdateAsync operation that
    // cannot be exercised with a mocked IRepository. Its behavior is covered by integration tests
    // against a real SQLite database in StorageRelationalServiceTests.

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

    #region 分块上传测试（FileChunkUploadService）

    [Fact]
    public async Task InitiateChunkedUploadAsync_CreatesUploadSession()
    {
        // Arrange
        var service = CreateChunkUploadService();
        var fileName = "large.jpg";
        var totalSize = 10 * 1024 * 1024; // 10MB
        var chunkSize = 5 * 1024 * 1024; // 5MB

        _mockUploadSessionRepository.Setup(r => r.InsertAsync(It.IsAny<FileUploadSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await service.InitiateChunkedUploadAsync(fileName, totalSize, chunkSize);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(fileName, result.Data.FileName);
        Assert.Equal(totalSize, result.Data.TotalSize);
        Assert.Equal(chunkSize, result.Data.ChunkSize);
        Assert.Equal(2, result.Data.TotalChunks);
        Assert.False(result.Data.IsCompleted);
    }

    [Fact]
    public async Task CancelChunkedUploadAsync_CancelsSession()
    {
        // Arrange
        var service = CreateChunkUploadService();
        var sessionId = Guid.NewGuid();
        var session = new FileUploadSession
        {
            Id = sessionId,
            IsCompleted = false,
            IsCancelled = false,
            CreatorId = TestHelper.DefaultTestUserId
        };
        var chunks = new[]
        {
            new FileChunk { ChunkPath = "path/to/chunk0" }
        };

        _mockUploadSessionRepository.Setup(r => r.GetAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        // Mock ToListAsync directly since Where is an extension method
        _mockChunkRepository.Setup(r => r.ToListAsync(It.IsAny<System.Linq.Expressions.Expression<Func<FileChunk, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunks.ToList());
        _mockStorage.Setup(s => s.DeleteAsync(It.IsAny<string>())).ReturnsAsync(true);
        _mockChunkRepository.Setup(r => r.DeleteAsync(It.IsAny<FileChunk>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockUploadSessionRepository.Setup(r => r.UpdateAsync(session, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await service.CancelChunkedUploadAsync(sessionId);

        // Assert
        Assert.True(session.IsCancelled);
    }

    [Fact]
    public async Task GetUploadProgressAsync_ReturnsProgress()
    {
        // Arrange
        var service = CreateChunkUploadService();
        var sessionId = Guid.NewGuid();
        var session = new FileUploadSession
        {
            Id = sessionId,
            FileName = "test.jpg",
            TotalSize = 10000,
            UploadedSize = 5000,
            TotalChunks = 2,
            UploadedChunks = 1,
            IsCompleted = false,
            IsCancelled = false,
            CreatorId = TestHelper.DefaultTestUserId
        };

        _mockUploadSessionRepository.Setup(r => r.GetAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Act
        var result = await service.GetUploadProgressAsync(sessionId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(sessionId, result.Data.UploadSessionId);
        Assert.Equal(10000, result.Data.TotalSize);
        Assert.Equal(5000, result.Data.UploadedSize);
        Assert.Equal(50.0, result.Data.ProgressPercentage, 1);
    }

    #endregion

    #region 分块上传测试（FileChunkUploadService）

    [Fact]
    public async Task InitiateChunkedUploadAsync_CreatesSession()
    {
        // Arrange
        var service = CreateChunkUploadService();
        var fileName = "large-file.zip";
        var totalSize = 50 * 1024 * 1024L; // 50MB
        var chunkSize = 5 * 1024 * 1024; // 5MB

        _mockUploadSessionRepository.Setup(r => r.InsertAsync(It.IsAny<FileUploadSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await service.InitiateChunkedUploadAsync(fileName, totalSize, chunkSize);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(fileName, result.Data.FileName);
        Assert.Equal(totalSize, result.Data.TotalSize);
        Assert.Equal(chunkSize, result.Data.ChunkSize);
        Assert.Equal(10, result.Data.TotalChunks); // 50MB / 5MB = 10 chunks
        Assert.Equal(0, result.Data.UploadedChunks);
        Assert.False(result.Data.IsCompleted);
        Assert.False(result.Data.IsCancelled);
        _mockUploadSessionRepository.Verify(r => r.InsertAsync(It.IsAny<FileUploadSession>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadChunkAsync_ThrowsException_WhenSessionInvalid()
    {
        // Arrange
        var service = CreateChunkUploadService();
        var uploadSessionId = Guid.NewGuid();
        var chunkIndex = 0;
        var chunkData = new byte[1024];
        var chunkStream = new MemoryStream(chunkData);

        // 会话不存在
        _mockUploadSessionRepository.Setup(r => r.GetAsync(uploadSessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileUploadSession?)null);

        // Act
        var result = await service.UploadChunkAsync(uploadSessionId, chunkIndex, chunkStream);

        // Assert
        Assert.False(result.Succeeded);
    }

    // 注意：UploadChunkAsync 和 CancelChunkedUploadAsync 的完整测试需要集成测试
    // 因为需要 Mock IQueryable 的异步方法（CountAsync、SumAsync、ToListAsync）

    /// <summary>
    /// 别人的上传会话：看不到进度、抢不了完成、也取消不掉。
    /// </summary>
    /// <remarks>
    /// 这四个端点此前的唯一判据就是「会话 id 对不对」。id 会经反向代理日志、共享的 HAR、
    /// 以及对象存储里 <c>chunk_{sessionId}_{index}</c> 这样的键名漏出去，拿到一个就能在
    /// 24 小时有效期内动别人的上传 —— 其中<b>抢先完成</b>最糟：合并出的文件记录会被盖上
    /// 抢的人的 <c>CreatorId</c>，他成了内容的所有者，而受害者的完成请求撞上「已完成」直接失败。
    /// </remarks>
    [Fact]
    public async Task ForeignUploadSession_IsRefusedOnEveryEndpoint()
    {
        var service = CreateChunkUploadService();
        var uploadSessionId = Guid.NewGuid();
        var session = new FileUploadSession
        {
            Id = uploadSessionId,
            FileName = "their-contract.pdf",
            TotalSize = 10000,
            TotalChunks = 10,
            UploadedChunks = 10,
            UploadedSize = 10000,
            IsCompleted = false,
            IsCancelled = false,
            CreatorId = Guid.Parse("99999999-9999-9999-9999-999999999999")
        };

        _mockUploadSessionRepository.Setup(r => r.GetAsync(uploadSessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var progress = await service.GetUploadProgressAsync(uploadSessionId);
        var upload = await service.UploadChunkAsync(uploadSessionId, 0, new MemoryStream(new byte[16]));
        var complete = await service.CompleteChunkedUploadAsync(uploadSessionId);

        Assert.False(progress.Succeeded);
        Assert.False(upload.Succeeded);
        Assert.False(complete.Succeeded);

        // 取消返回的是成功形状（与「本来就不存在」同一句回答，刻意不区分），
        // 所以这里断言的是**状态**：会话没有被取消掉。
        var cancel = await service.CancelChunkedUploadAsync(uploadSessionId);
        Assert.True(cancel.Succeeded);
        Assert.False(session.IsCancelled);
    }

    [Fact]
    public async Task GetUploadProgressAsync_ReturnsProgress_MidUpload()
    {
        // Arrange
        var service = CreateChunkUploadService();
        var uploadSessionId = Guid.NewGuid();
        var session = new FileUploadSession
        {
            Id = uploadSessionId,
            FileName = "test.zip",
            TotalSize = 10000,
            TotalChunks = 10,
            UploadedChunks = 5,
            UploadedSize = 5000,
            IsCompleted = false,
            IsCancelled = false,
            CreatorId = TestHelper.DefaultTestUserId
        };

        _mockUploadSessionRepository.Setup(r => r.GetAsync(uploadSessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Act
        var result = await service.GetUploadProgressAsync(uploadSessionId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(uploadSessionId, result.Data.UploadSessionId);
        Assert.Equal(10000, result.Data.TotalSize);
        Assert.Equal(5000, result.Data.UploadedSize);
        Assert.Equal(10, result.Data.TotalChunks);
        Assert.Equal(5, result.Data.UploadedChunks);
        Assert.Equal(50.0, result.Data.ProgressPercentage);
        Assert.False(result.Data.IsCompleted);
    }

    [Fact]
    public async Task CancelChunkedUploadAsync_ReturnsEarly_WhenSessionCompleted()
    {
        // Arrange
        var service = CreateChunkUploadService();
        var uploadSessionId = Guid.NewGuid();
        var session = new FileUploadSession
        {
            Id = uploadSessionId,
            IsCompleted = true, // 已完成的会话
            IsCancelled = false
        };

        _mockUploadSessionRepository.Setup(r => r.GetAsync(uploadSessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Act
        await service.CancelChunkedUploadAsync(uploadSessionId);

        // Assert - 应该提前返回，不执行任何操作
        // 注意：完整测试需要集成测试，因为需要 Mock IQueryable 的异步方法
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 造一个「一定对不上」的口令哈希：真实实现用的是加盐 HMAC-SHA256 的 <c>salt:hash</c>，
    /// 这里的裸 SHA256 与它格式都不同，用例要的正是「不匹配」。逐字保留自拆分前的夹具。
    /// </summary>
    private string ComputePasswordHash(string password)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    #endregion
}
