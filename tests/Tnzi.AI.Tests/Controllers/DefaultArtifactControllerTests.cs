using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Tnzi.AI.Controllers;
using Tnzi.AspNetCore.Models;
using Tnzi.Exceptions;

namespace Tnzi.AI.Tests.Controllers;

/// <summary>
/// DefaultArtifactController 单元测试 — 覆盖线程所有权 A1 安全修复的授权执行
/// </summary>
public class DefaultArtifactControllerTests
{
    private readonly Mock<IAgentArtifactService> _artifactService = new();
    private readonly Mock<IAgentThreadService> _threadService = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly DefaultArtifactController _controller;
    private readonly Guid _userId = Guid.NewGuid();

    public DefaultArtifactControllerTests()
    {
        _controller = new DefaultArtifactController(_artifactService.Object, _threadService.Object);

        _currentUser.Setup(u => u.Id).Returns(_userId);
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);

        var serviceProvider = new Mock<IServiceProvider>();
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        serviceProvider.Setup(sp => sp.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);
        serviceProvider.Setup(sp => sp.GetService(typeof(ICurrentUser))).Returns(_currentUser.Object);

        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider.Object };
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    // -------------------------------------------------------------------------
    // GetByThread — ownership enforcement
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetByThread_Owner_ReturnsArtifacts()
    {
        var threadId = Guid.NewGuid();
        var artifacts = new List<AgentArtifactDto>
        {
            new() { Id = Guid.NewGuid(), ThreadId = threadId, FileName = "report.md" }
        };
        _threadService.Setup(s => s.IsOwnerAsync(threadId, _userId)).ReturnsAsync(true);
        _artifactService.Setup(s => s.GetByThreadAsync(threadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<AgentArtifactDto>>.Success(artifacts));

        var result = await _controller.GetByThread(threadId);

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.Count.ShouldBe(1);
        result.Data![0].FileName.ShouldBe("report.md");
    }

    [Fact]
    public async Task GetByThread_NotOwner_Throws404()
    {
        // A1 fix: foreign user must receive 404 (ThreadNotFound), not 200 or 403
        var threadId = Guid.NewGuid();
        _threadService.Setup(s => s.IsOwnerAsync(threadId, _userId)).ReturnsAsync(false);

        var ex = await Should.ThrowAsync<BusinessException>(
            () => _controller.GetByThread(threadId));

        ex.HttpStatusCode.ShouldBe(404);
        ex.Code.ShouldBe(Tnzi.AI.ErrorCodes.ThreadNotFound);
        // Artifact service must NOT be called — ownership check short-circuits
        _artifactService.Verify(s => s.GetByThreadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetByThread_ForeignThread_ArtifactServiceNeverCalled()
    {
        // Additional guard: a foreign thread must short-circuit before reaching the service
        var foreignThreadId = Guid.NewGuid();
        _threadService.Setup(s => s.IsOwnerAsync(foreignThreadId, _userId)).ReturnsAsync(false);

        await Should.ThrowAsync<BusinessException>(() => _controller.GetByThread(foreignThreadId));

        _artifactService.Verify(s => s.GetByThreadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -------------------------------------------------------------------------
    // GetById — ownership enforcement
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetById_Owner_ReturnsArtifact()
    {
        var artifactId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var dto = new AgentArtifactDto { Id = artifactId, ThreadId = threadId, FileName = "data.csv" };
        _artifactService.Setup(s => s.GetByIdAsync(artifactId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AgentArtifactDto>.Success(dto));
        _threadService.Setup(s => s.IsOwnerAsync(threadId, _userId)).ReturnsAsync(true);

        var result = await _controller.GetById(artifactId);

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.FileName.ShouldBe("data.csv");
    }

    [Fact]
    public async Task GetById_ArtifactBelongsToForeignThread_Throws404()
    {
        // A1 fix: even if the artifact ID is valid, a foreign thread must yield 404
        var artifactId = Guid.NewGuid();
        var foreignThreadId = Guid.NewGuid();
        var dto = new AgentArtifactDto { Id = artifactId, ThreadId = foreignThreadId, FileName = "secret.md" };
        _artifactService.Setup(s => s.GetByIdAsync(artifactId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AgentArtifactDto>.Success(dto));
        _threadService.Setup(s => s.IsOwnerAsync(foreignThreadId, _userId)).ReturnsAsync(false);

        var ex = await Should.ThrowAsync<BusinessException>(
            () => _controller.GetById(artifactId));

        ex.HttpStatusCode.ShouldBe(404);
        ex.Code.ShouldBe(Tnzi.AI.ErrorCodes.ThreadNotFound);
    }

    [Fact]
    public async Task GetById_ArtifactNotFound_ReturnsFailedResult()
    {
        var artifactId = Guid.NewGuid();
        _artifactService.Setup(s => s.GetByIdAsync(artifactId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AgentArtifactDto>.Failure("Artifact not found.", 404));

        var result = await _controller.GetById(artifactId);

        result.Succeeded.ShouldBeFalse();
        // Ownership check must NOT be called when artifact does not exist
        _threadService.Verify(s => s.IsOwnerAsync(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
    }

    // -------------------------------------------------------------------------
    // GetByRun — owner-scoped delegation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetByRun_DelegatesCurrentUserIdToService()
    {
        var runId = Guid.NewGuid();
        _artifactService.Setup(s => s.GetByRunAsync(runId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<AgentArtifactDto>>.Success([]));

        var result = await _controller.GetByRun(runId);

        result.Succeeded.ShouldBeTrue();
        _artifactService.Verify(s => s.GetByRunAsync(runId, _userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // -------------------------------------------------------------------------
    // Unauthenticated user
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetByThread_UserIdIsNull_Throws401()
    {
        _currentUser.Setup(u => u.Id).Returns((Guid?)null);

        var ex = await Should.ThrowAsync<BusinessException>(
            () => _controller.GetByThread(Guid.NewGuid()));

        ex.HttpStatusCode.ShouldBe(401);
        ex.Code.ShouldBe(Tnzi.Exceptions.ErrorCodes.UNAUTHORIZED);
    }

    [Fact]
    public async Task GetById_UserIdIsNull_Throws401()
    {
        _currentUser.Setup(u => u.Id).Returns((Guid?)null);
        var artifactId = Guid.NewGuid();
        var dto = new AgentArtifactDto { Id = artifactId, ThreadId = Guid.NewGuid() };
        _artifactService.Setup(s => s.GetByIdAsync(artifactId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AgentArtifactDto>.Success(dto));

        var ex = await Should.ThrowAsync<BusinessException>(
            () => _controller.GetById(artifactId));

        ex.HttpStatusCode.ShouldBe(401);
        ex.Code.ShouldBe(Tnzi.Exceptions.ErrorCodes.UNAUTHORIZED);
    }
}
