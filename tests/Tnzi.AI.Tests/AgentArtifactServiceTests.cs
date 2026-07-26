namespace Tnzi.AI.Tests;

/// <summary>
/// AgentArtifact 服务测试
/// </summary>
public class AgentArtifactServiceTests
{
    private readonly Mock<IRepository<AgentArtifact, Guid>> _artifactRepo;
    private readonly Mock<IRepository<AgentThread, Guid>> _threadRepo;
    private readonly IServiceProvider _serviceProvider;

    public AgentArtifactServiceTests()
    {
        _artifactRepo = new Mock<IRepository<AgentArtifact, Guid>>();
        _threadRepo = new Mock<IRepository<AgentThread, Guid>>();

        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();
    }

    private AgentArtifactService CreateService() => new(_serviceProvider, _artifactRepo.Object, _threadRepo.Object);

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
    public async Task GetByRunAsync_WithOwner_ReturnsMatchingRunArtifacts()
    {
        // Arrange - verify that only artifacts whose RunId matches AND ThreadId is owned are returned
        var ownerUserId = Guid.NewGuid();
        var ownedThreadId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var otherRunId = Guid.NewGuid();

        _threadRepo
            .Setup(r => r.SelectAsync(
                It.IsAny<Expression<Func<AgentThread, Guid>>>(),
                It.IsAny<Expression<Func<AgentThread, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { ownedThreadId });

        // Only the matching-run artifact is returned; artifact with different runId is excluded
        // The mock honors the predicate by returning only the expected subset
        var matchingArtifact = new AgentArtifact
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            ThreadId = ownedThreadId,
            FileName = "output.txt",
            VirtualPath = "/mnt/outputs/output.txt"
        };
        _artifactRepo
            .Setup(r => r.ToListAsync(
                It.Is<Expression<Func<AgentArtifact, bool>>>(pred =>
                    pred.Compile()(matchingArtifact) == true &&
                    pred.Compile()(new AgentArtifact { RunId = otherRunId, ThreadId = ownedThreadId }) == false &&
                    pred.Compile()(new AgentArtifact { RunId = runId, ThreadId = Guid.NewGuid() }) == false),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AgentArtifact> { matchingArtifact });

        // Act
        var result = await CreateService().GetByRunAsync(runId, ownerUserId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Single(result.Data!);
        Assert.Equal("output.txt", result.Data![0].FileName);
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

    // -------------------------------------------------------------------------
    // GetByRunAsync owner-scoped overload (A1) - predicate-honoring mocks
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetByRunAsync_WithOwner_ReturnsOnlyOwnerArtifacts()
    {
        // Arrange - predicate must include RunId AND ownedThreadId; foreign thread excluded
        var ownerUserId = Guid.NewGuid();
        var ownedThreadId = Guid.NewGuid();
        var foreignThreadId = Guid.NewGuid();
        var runId = Guid.NewGuid();

        _threadRepo
            .Setup(r => r.SelectAsync(
                It.IsAny<Expression<Func<AgentThread, Guid>>>(),
                It.IsAny<Expression<Func<AgentThread, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { ownedThreadId });

        var ownedArtifact = new AgentArtifact { Id = Guid.NewGuid(), RunId = runId, ThreadId = ownedThreadId, FileName = "owned.txt", VirtualPath = "/mnt/owned.txt" };
        var foreignArtifact = new AgentArtifact { Id = Guid.NewGuid(), RunId = runId, ThreadId = foreignThreadId, FileName = "foreign.txt", VirtualPath = "/mnt/foreign.txt" };
        var wrongRunArtifact = new AgentArtifact { Id = Guid.NewGuid(), RunId = Guid.NewGuid(), ThreadId = ownedThreadId, FileName = "wrong-run.txt", VirtualPath = "/mnt/wrong.txt" };

        // Mock honors the predicate: only ownedArtifact passes (RunId match + owned thread)
        _artifactRepo
            .Setup(r => r.ToListAsync(
                It.Is<Expression<Func<AgentArtifact, bool>>>(pred =>
                    pred.Compile()(ownedArtifact) == true &&       // owner's run artifact - included
                    pred.Compile()(foreignArtifact) == false &&    // foreign thread - excluded
                    pred.Compile()(wrongRunArtifact) == false),    // wrong RunId - excluded
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AgentArtifact> { ownedArtifact });

        // Act
        var result = await CreateService().GetByRunAsync(runId, ownerUserId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Single(result.Data!);
        Assert.Equal("owned.txt", result.Data![0].FileName);
    }

    [Fact]
    public async Task GetByRunAsync_WithOwner_OtherUserGetsEmpty()
    {
        // Arrange - user B owns no threads; predicate must therefore exclude all artifacts
        var userBId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var someArtifact = new AgentArtifact { Id = Guid.NewGuid(), RunId = runId, ThreadId = Guid.NewGuid(), FileName = "other.txt", VirtualPath = "/mnt/other.txt" };

        // Empty owned-thread list → any artifact fails the Contains check
        _threadRepo
            .Setup(r => r.SelectAsync(
                It.IsAny<Expression<Func<AgentThread, Guid>>>(),
                It.IsAny<Expression<Func<AgentThread, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        // Predicate must reject someArtifact (its ThreadId not in empty list)
        _artifactRepo
            .Setup(r => r.ToListAsync(
                It.Is<Expression<Func<AgentArtifact, bool>>>(pred =>
                    pred.Compile()(someArtifact) == false),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AgentArtifact>());

        // Act
        var result = await CreateService().GetByRunAsync(runId, userBId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Empty(result.Data!);
    }

    [Fact]
    public async Task GetByRunAsync_WithOwner_EmptyRunId_ReturnsThreadScopedArtifacts()
    {
        // Arrange - RunId=Guid.Empty skips RunId filter; only ThreadId ownership matters
        var ownerUserId = Guid.NewGuid();
        var ownedThreadId = Guid.NewGuid();
        var foreignThreadId = Guid.NewGuid();

        _threadRepo
            .Setup(r => r.SelectAsync(
                It.IsAny<Expression<Func<AgentThread, Guid>>>(),
                It.IsAny<Expression<Func<AgentThread, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { ownedThreadId });

        var ownedArtifact = new AgentArtifact { Id = Guid.NewGuid(), RunId = Guid.Empty, ThreadId = ownedThreadId, FileName = "present.txt", VirtualPath = "/mnt/present.txt" };
        var foreignArtifact = new AgentArtifact { Id = Guid.NewGuid(), RunId = Guid.Empty, ThreadId = foreignThreadId, FileName = "foreign.txt", VirtualPath = "/mnt/foreign.txt" };
        var runArtifact = new AgentArtifact { Id = Guid.NewGuid(), RunId = Guid.NewGuid(), ThreadId = ownedThreadId, FileName = "run.txt", VirtualPath = "/mnt/run.txt" };

        // For Guid.Empty the predicate is: ownedThreadIds.Contains(e.ThreadId) - no RunId filter
        _artifactRepo
            .Setup(r => r.ToListAsync(
                It.Is<Expression<Func<AgentArtifact, bool>>>(pred =>
                    pred.Compile()(ownedArtifact) == true &&       // owned thread - included
                    pred.Compile()(foreignArtifact) == false &&    // foreign thread - excluded
                    pred.Compile()(runArtifact) == true),          // owned thread, non-empty RunId still passes (no RunId filter when empty)
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AgentArtifact> { ownedArtifact });

        // Act
        var result = await CreateService().GetByRunAsync(Guid.Empty, ownerUserId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Single(result.Data!);
        Assert.Equal("present.txt", result.Data![0].FileName);
    }
}
