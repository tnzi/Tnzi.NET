namespace Tnzi.AI.Tests.Middleware;

/// <summary>
/// FileUploadMiddleware 单元测试
/// </summary>
public class FileUploadMiddlewareTests : IDisposable
{
    private readonly string _uploadsDir;

    public FileUploadMiddlewareTests()
    {
        _uploadsDir = Path.Combine(Path.GetTempPath(), $"tnzi-upload-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_uploadsDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_uploadsDir))
            Directory.Delete(_uploadsDir, recursive: true);
    }

    #region Helper

    private static FileUploadMiddleware CreateMiddleware()
    {
        return new FileUploadMiddleware(Mock.Of<ILogger<FileUploadMiddleware>>());
    }

    private AiMiddlewareContext CreateContext(
        List<FileAttachment>? attachments = null,
        bool setUploadsDir = true)
    {
        var context = new AiMiddlewareContext
        {
            Request = new AgentRunRequest
            {
                UserMessage = "Analyze this file",
                Attachments = attachments,
                ThreadId = Guid.NewGuid()
            },
            Agent = AgentResolution.Success(agent: null!, provider: "test", model: "test", agentId: null),
            Messages = [new ChatMessage(ChatRole.User, "Analyze this file")],
            ServiceProvider = new ServiceCollection().BuildServiceProvider()
        };

        if (setUploadsDir)
            context.Properties[FileUploadMiddleware.ThreadUploadsDirKey] = _uploadsDir;

        return context;
    }

    private static AgentRunResult CreateSuccessResult()
    {
        return new AgentRunResult { Response = "ok" };
    }

    private string CreateTempFile(string fileName, string content = "test content")
    {
        var filePath = Path.Combine(_uploadsDir, fileName);
        File.WriteAllText(filePath, content);
        return filePath;
    }

    #endregion

    #region InvokeAsync — No attachments

    [Fact]
    public async Task InvokeAsync_NoAttachments_PassesThroughWithoutInjection()
    {
        // Arrange
        var middleware = CreateMiddleware();
        var context = CreateContext(attachments: null);
        var originalMessageCount = context.Messages.Count;

        AiMiddlewareDelegate next = (ctx, ct) => Task.FromResult(CreateSuccessResult());

        // Act
        var result = await middleware.InvokeAsync(context, next);

        // Assert
        result.Response.ShouldBe("ok");
        context.Messages.Count.ShouldBe(originalMessageCount);
        context.Messages.ShouldNotContain(m => m.Text != null && m.Text.Contains("<uploaded_files>"));
    }

    [Fact]
    public async Task InvokeAsync_EmptyAttachments_PassesThroughWithoutInjection()
    {
        // Arrange
        var middleware = CreateMiddleware();
        var context = CreateContext(attachments: []);
        var originalMessageCount = context.Messages.Count;

        AiMiddlewareDelegate next = (ctx, ct) => Task.FromResult(CreateSuccessResult());

        // Act
        var result = await middleware.InvokeAsync(context, next);

        // Assert
        context.Messages.Count.ShouldBe(originalMessageCount);
    }

    #endregion

    #region InvokeAsync — Valid attachment

    [Fact]
    public async Task InvokeAsync_ValidAttachment_InjectsUploadedFilesBlock()
    {
        // Arrange
        var middleware = CreateMiddleware();
        CreateTempFile("readme.txt", "hello world");

        var context = CreateContext(attachments:
        [
            new FileAttachment("readme.txt", 11, "text/plain")
        ]);

        AiMiddlewareDelegate next = (ctx, ct) => Task.FromResult(CreateSuccessResult());

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        context.Messages.Count.ShouldBe(2); // system + user
        var systemMsg = context.Messages[0];
        systemMsg.Role.ShouldBe(ChatRole.System);
        systemMsg.Text.ShouldNotBeNull();
        systemMsg.Text.ShouldContain("<uploaded_files>");
        systemMsg.Text.ShouldContain("readme.txt");
        systemMsg.Text.ShouldContain("text/plain");
        systemMsg.Text.ShouldContain("</uploaded_files>");
    }

    #endregion

    #region InvokeAsync — Path traversal rejection

    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("..\\windows\\system32\\config")]
    [InlineData("sub/dir/file.txt")]
    [InlineData("sub\\dir\\file.txt")]
    public async Task InvokeAsync_PathTraversalInFileName_RejectsFile(string maliciousFileName)
    {
        // Arrange
        var middleware = CreateMiddleware();
        var context = CreateContext(attachments:
        [
            new FileAttachment(maliciousFileName, 100, "text/plain")
        ]);

        AiMiddlewareDelegate next = (ctx, ct) => Task.FromResult(CreateSuccessResult());

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert — no system message injected (file rejected)
        context.Messages.Count.ShouldBe(1); // only original user message
        context.Messages[0].Role.ShouldBe(ChatRole.User);
    }

    #endregion

    #region InvokeAsync — File not on disk

    [Fact]
    public async Task InvokeAsync_FileNotOnDisk_SkipsFile()
    {
        // Arrange
        var middleware = CreateMiddleware();
        var context = CreateContext(attachments:
        [
            new FileAttachment("nonexistent.txt", 100, "text/plain")
        ]);

        AiMiddlewareDelegate next = (ctx, ct) => Task.FromResult(CreateSuccessResult());

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert — no system message injected (file not found)
        context.Messages.Count.ShouldBe(1);
    }

    #endregion

    #region InvokeStreamingAsync

    [Fact]
    public async Task InvokeStreamingAsync_ValidAttachment_InjectsBeforeYielding()
    {
        // Arrange
        var middleware = CreateMiddleware();
        CreateTempFile("data.csv", "col1,col2");

        var context = CreateContext(attachments:
        [
            new FileAttachment("data.csv", 9, "text/csv")
        ]);

        var expectedChunk = new AgentStreamChunk { Text = "streamed" };
        AiStreamingMiddlewareDelegate next = (ctx, ct) => ToAsyncEnumerable(expectedChunk);

        // Act
        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in middleware.InvokeStreamingAsync(context, next))
        {
            chunks.Add(chunk);
        }

        // Assert
        chunks.Count.ShouldBe(1);
        context.Messages.Count.ShouldBe(2);
        context.Messages[0].Role.ShouldBe(ChatRole.System);
        context.Messages[0].Text.ShouldContain("data.csv");
    }

    #endregion

    #region InvokeAsync — No ThreadUploadsDir

    [Fact]
    public async Task InvokeAsync_NoThreadUploadsDir_SkipsInjection()
    {
        // Arrange
        var middleware = CreateMiddleware();
        var context = CreateContext(
            attachments: [new FileAttachment("readme.txt", 5, "text/plain")],
            setUploadsDir: false);

        AiMiddlewareDelegate next = (ctx, ct) => Task.FromResult(CreateSuccessResult());

        // Act
        await middleware.InvokeAsync(context, next);

        // Assert
        context.Messages.Count.ShouldBe(1); // no injection
    }

    #endregion

    #region Async Helper

    private static async IAsyncEnumerable<AgentStreamChunk> ToAsyncEnumerable(params AgentStreamChunk[] chunks)
    {
        foreach (var chunk in chunks)
        {
            yield return chunk;
        }

        await Task.CompletedTask;
    }

    #endregion
}
