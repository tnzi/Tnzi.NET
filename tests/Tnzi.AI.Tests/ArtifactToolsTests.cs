namespace Tnzi.AI.Tests;

public class ArtifactToolsTests
{
    private readonly Mock<IAgentArtifactService> _artifactService;
    private readonly Mock<IAgentExecutionContextAccessor> _contextAccessor;
    private readonly ArtifactTools _tools;

    public ArtifactToolsTests()
    {
        _artifactService = new Mock<IAgentArtifactService>();
        _contextAccessor = new Mock<IAgentExecutionContextAccessor>();
        _contextAccessor.Setup(a => a.CurrentRequest).Returns(new AgentRunRequest { ThreadId = Guid.NewGuid() });

        _tools = new ArtifactTools(_artifactService.Object, _contextAccessor.Object);
    }

    [Fact]
    public async Task PresentFiles_ValidPaths_CreatesArtifacts()
    {
        // Arrange
        var paths = new List<string> { "/mnt/outputs/report.md", "/mnt/outputs/data.json" };
        _artifactService
            .Setup(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AgentArtifactDto()));

        // Act
        var result = await _tools.PresentFilesAsync(paths);

        // Assert
        Assert.Contains("Successfully presented 2 file(s)", result);
        Assert.Contains("report.md", result);
        Assert.Contains("data.json", result);
        _artifactService.Verify(s => s.CreateAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(),
            "/mnt/outputs/report.md", "report.md", "text/markdown", null,
            It.IsAny<CancellationToken>()), Times.Once);
        _artifactService.Verify(s => s.CreateAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(),
            "/mnt/outputs/data.json", "data.json", "application/json", null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PresentFiles_EmptyList_ReturnsMessage()
    {
        var result = await _tools.PresentFilesAsync([]);
        Assert.Equal("No files to present.", result);
    }

    [Fact]
    public async Task PresentFiles_NullList_ReturnsMessage()
    {
        var result = await _tools.PresentFilesAsync(null!);
        Assert.Equal("No files to present.", result);
    }

    [Fact]
    public async Task PresentFiles_NoThreadContext_ReturnsError()
    {
        // Arrange — no thread
        _contextAccessor.Setup(a => a.CurrentRequest).Returns(new AgentRunRequest { ThreadId = null });
        var tools = new ArtifactTools(_artifactService.Object, _contextAccessor.Object);

        // Act
        var result = await tools.PresentFilesAsync(["/mnt/outputs/file.txt"]);

        // Assert
        Assert.Contains("No active thread context", result);
    }

    [Fact]
    public async Task PresentFiles_InvalidPath_ReportsError()
    {
        // Arrange
        _artifactService
            .Setup(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AgentArtifactDto()));

        // Act — mix of valid and invalid paths
        var result = await _tools.PresentFilesAsync(["/mnt/outputs/good.txt", ""]);

        // Assert
        Assert.Contains("Successfully presented 1 file(s)", result);
        Assert.Contains("Invalid path", result);
    }

    [Fact]
    public async Task PresentFiles_MimeTypeInference_Correct()
    {
        // Arrange
        _artifactService
            .Setup(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AgentArtifactDto()));

        // Act
        await _tools.PresentFilesAsync(["/output/image.png"]);

        // Assert — verify MIME type was inferred as image/png
        _artifactService.Verify(s => s.CreateAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(),
            "/output/image.png", "image.png", "image/png", null,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
