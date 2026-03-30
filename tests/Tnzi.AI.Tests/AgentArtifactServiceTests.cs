namespace Tnzi.AI.Tests;

/// <summary>
/// AgentArtifact 服务测试
/// </summary>
public class AgentArtifactServiceTests
{
    private readonly Mock<IRepository<AgentArtifact, Guid>> _artifactRepo;
    private readonly IServiceProvider _serviceProvider;

    public AgentArtifactServiceTests()
    {
        _artifactRepo = new Mock<IRepository<AgentArtifact, Guid>>();

        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();
    }

    private AgentArtifactService CreateService() => new(_serviceProvider, _artifactRepo.Object);

    [Fact]
    public async Task CreateAsync_NewArtifact_ReturnsOk()
    {
        // Arrange
        _artifactRepo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<AgentArtifact, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentArtifact?)null);
        _artifactRepo.Setup(r => r.InsertAsync(It.IsAny<AgentArtifact>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var runId = Guid.NewGuid();
        var threadId = Guid.NewGuid();

        // Act
        var result = await CreateService().CreateAsync(runId, threadId,
            "/mnt/outputs/report.md", "report.md", "text/markdown", 1024);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal("report.md", result.Data!.FileName);
        Assert.Equal("text/markdown", result.Data.ContentType);
        Assert.Equal(1024, result.Data.Size);
    }

    [Fact]
    public async Task CreateAsync_DuplicateVirtualPath_UpdatesExisting()
    {
        // Arrange
        var existing = new AgentArtifact
        {
            Id = Guid.NewGuid(),
            RunId = Guid.NewGuid(),
            ThreadId = Guid.NewGuid(),
            VirtualPath = "/mnt/outputs/report.md",
            FileName = "report.md"
        };
        _artifactRepo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<AgentArtifact, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        // Act
        var result = await CreateService().CreateAsync(existing.RunId, existing.ThreadId,
            "/mnt/outputs/report.md", "report.md", "text/markdown", 2048);

        // Assert
        Assert.True(result.Succeeded);
        _artifactRepo.Verify(r => r.UpdateAsync(It.Is<AgentArtifact>(e => e.Size == 2048), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetByThreadAsync_ReturnsArtifacts()
    {
        // Arrange
        var threadId = Guid.NewGuid();
        var artifacts = new List<AgentArtifact>
        {
            new() { Id = Guid.NewGuid(), ThreadId = threadId, FileName = "a.md", VirtualPath = "/mnt/outputs/a.md" },
            new() { Id = Guid.NewGuid(), ThreadId = threadId, FileName = "b.pdf", VirtualPath = "/mnt/outputs/b.pdf" }
        };
        _artifactRepo.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<AgentArtifact, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(artifacts);

        // Act
        var result = await CreateService().GetByThreadAsync(threadId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.Count);
    }

    [Fact]
    public async Task GetByRunAsync_ReturnsArtifacts()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var artifacts = new List<AgentArtifact>
        {
            new() { Id = Guid.NewGuid(), RunId = runId, FileName = "output.txt", VirtualPath = "/mnt/outputs/output.txt" }
        };
        _artifactRepo.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<AgentArtifact, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(artifacts);

        // Act
        var result = await CreateService().GetByRunAsync(runId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Single(result.Data!);
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsFail()
    {
        // Arrange
        _artifactRepo.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentArtifact?)null);

        // Act
        var result = await CreateService().GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.True(result.Failed);
    }
}
