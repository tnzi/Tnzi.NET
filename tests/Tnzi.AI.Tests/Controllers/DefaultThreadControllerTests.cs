using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Tnzi.AI.Controllers;
using Tnzi.AspNetCore.Extensions;
using Tnzi.AspNetCore.Models;
using Tnzi.Exceptions;
using Tnzi.Data;

namespace Tnzi.AI.Tests.Controllers;

/// <summary>
/// DefaultThreadController 单元测试 — 覆盖参数传递、所有权验证、Result → ApiResult 转换
/// </summary>
public class DefaultThreadControllerTests
{
    private readonly Mock<IAgentThreadService> _threadService = new();
    private readonly Mock<IMessageFeedbackService> _feedbackService = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly DefaultThreadController _controller;
    private readonly Guid _userId = Guid.NewGuid();

    public DefaultThreadControllerTests()
    {
        _controller = new DefaultThreadController(_threadService.Object, _feedbackService.Object);

        // 设置已认证用户
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
    // GetMyThreads
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetMyThreads_SetsCreatorIdToCurrentUser_AndCallsService()
    {
        var query = new ThreadListQueryDto();
        var pagedList = new PagedList<AgentThreadDto>([], 1, 10, 0);
        _threadService.Setup(s => s.GetListAsync(It.IsAny<ThreadListQueryDto>()))
            .ReturnsAsync(Result<IPagedList<AgentThreadDto>>.Success(pagedList));

        var result = await _controller.GetMyThreads(query);

        result.Succeeded.ShouldBeTrue();
        // 验证 CreatorId 被设置为当前用户
        _threadService.Verify(s => s.GetListAsync(
            It.Is<ThreadListQueryDto>(q => q.CreatorId == _userId)), Times.Once);
    }

    [Fact]
    public async Task GetMyThreads_ServiceReturnsFailure_ReturnsFailedApiResult()
    {
        var query = new ThreadListQueryDto();
        _threadService.Setup(s => s.GetListAsync(It.IsAny<ThreadListQueryDto>()))
            .ReturnsAsync(Result<IPagedList<AgentThreadDto>>.Failure("Error", 500));

        var result = await _controller.GetMyThreads(query);

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe("Error");
    }

    // -------------------------------------------------------------------------
    // GetDetail
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetDetail_OwnershipValid_ReturnsDetail()
    {
        var threadId = Guid.NewGuid();
        var detail = new AgentThreadDetailDto { Id = threadId, Title = "Test" };
        _threadService.Setup(s => s.IsOwnerAsync(threadId, _userId)).ReturnsAsync(true);
        _threadService.Setup(s => s.GetDetailAsync(threadId, 50))
            .ReturnsAsync(Result<AgentThreadDetailDto>.Success(detail));

        var result = await _controller.GetDetail(threadId);

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.Id.ShouldBe(threadId);
    }

    [Fact]
    public async Task GetDetail_CustomMessageLimit_PassedToService()
    {
        var threadId = Guid.NewGuid();
        _threadService.Setup(s => s.IsOwnerAsync(threadId, _userId)).ReturnsAsync(true);
        _threadService.Setup(s => s.GetDetailAsync(threadId, 100))
            .ReturnsAsync(Result<AgentThreadDetailDto>.Success(new AgentThreadDetailDto()));

        await _controller.GetDetail(threadId, messageLimit: 100);

        _threadService.Verify(s => s.GetDetailAsync(threadId, 100), Times.Once);
    }

    [Fact]
    public async Task GetDetail_NotOwner_ThrowsBusinessException()
    {
        var threadId = Guid.NewGuid();
        _threadService.Setup(s => s.IsOwnerAsync(threadId, _userId)).ReturnsAsync(false);

        var ex = await Should.ThrowAsync<BusinessException>(() => _controller.GetDetail(threadId));
        ex.HttpStatusCode.ShouldBe(404);
    }

    // -------------------------------------------------------------------------
    // UpdateTitle
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateTitle_OwnershipValid_CallsServiceWithTitle()
    {
        var threadId = Guid.NewGuid();
        var input = new UpdateThreadTitleDto { Title = "New Title" };
        _threadService.Setup(s => s.IsOwnerAsync(threadId, _userId)).ReturnsAsync(true);
        _threadService.Setup(s => s.UpdateTitleAsync(threadId, "New Title"))
            .ReturnsAsync(Result<AgentThreadDto>.Success(new AgentThreadDto { Title = "New Title" }));

        var result = await _controller.UpdateTitle(threadId, input);

        result.Succeeded.ShouldBeTrue();
        result.Data!.Title.ShouldBe("New Title");
    }

    [Fact]
    public async Task UpdateTitle_NotOwner_ThrowsBusinessException()
    {
        var threadId = Guid.NewGuid();
        _threadService.Setup(s => s.IsOwnerAsync(threadId, _userId)).ReturnsAsync(false);

        var ex = await Should.ThrowAsync<BusinessException>(
            () => _controller.UpdateTitle(threadId, new UpdateThreadTitleDto { Title = "X" }));
        ex.HttpStatusCode.ShouldBe(404);
    }

    // -------------------------------------------------------------------------
    // Delete
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Delete_OwnershipValid_CallsService()
    {
        var threadId = Guid.NewGuid();
        _threadService.Setup(s => s.IsOwnerAsync(threadId, _userId)).ReturnsAsync(true);
        _threadService.Setup(s => s.DeleteAsync(threadId)).ReturnsAsync(Result.Success());

        var result = await _controller.Delete(threadId);

        result.Succeeded.ShouldBeTrue();
        _threadService.Verify(s => s.DeleteAsync(threadId), Times.Once);
    }

    [Fact]
    public async Task Delete_NotOwner_ThrowsBusinessException()
    {
        var threadId = Guid.NewGuid();
        _threadService.Setup(s => s.IsOwnerAsync(threadId, _userId)).ReturnsAsync(false);

        await Should.ThrowAsync<BusinessException>(() => _controller.Delete(threadId));
    }

    // -------------------------------------------------------------------------
    // ExportAsJson
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExportAsJson_OwnershipValid_ReturnsExportData()
    {
        var threadId = Guid.NewGuid();
        var exportDto = new ThreadExportDto { Id = threadId };
        _threadService.Setup(s => s.IsOwnerAsync(threadId, _userId)).ReturnsAsync(true);
        _threadService.Setup(s => s.ExportAsJsonAsync(threadId))
            .ReturnsAsync(Result<ThreadExportDto>.Success(exportDto));

        var result = await _controller.ExportAsJson(threadId);

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.Id.ShouldBe(threadId);
    }

    [Fact]
    public async Task ExportAsJson_NotOwner_ThrowsBusinessException()
    {
        var threadId = Guid.NewGuid();
        _threadService.Setup(s => s.IsOwnerAsync(threadId, _userId)).ReturnsAsync(false);

        await Should.ThrowAsync<BusinessException>(() => _controller.ExportAsJson(threadId));
    }

    // -------------------------------------------------------------------------
    // ExportAsMarkdown
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExportAsMarkdown_Success_ReturnsFileResult()
    {
        var threadId = Guid.NewGuid();
        _threadService.Setup(s => s.IsOwnerAsync(threadId, _userId)).ReturnsAsync(true);
        _threadService.Setup(s => s.ExportAsMarkdownAsync(threadId))
            .ReturnsAsync(Result<string>.Success("# Thread"));

        var result = await _controller.ExportAsMarkdown(threadId);

        result.ShouldBeOfType<FileContentResult>();
        var fileResult = (FileContentResult)result;
        fileResult.ContentType.ShouldBe("text/markdown");
        fileResult.FileDownloadName.ShouldBe($"thread-{threadId}.md");
    }

    [Fact]
    public async Task ExportAsMarkdown_ServiceFails_ReturnsNotFound()
    {
        var threadId = Guid.NewGuid();
        _threadService.Setup(s => s.IsOwnerAsync(threadId, _userId)).ReturnsAsync(true);
        _threadService.Setup(s => s.ExportAsMarkdownAsync(threadId))
            .ReturnsAsync(Result<string>.Failure("Not found", 404));

        var result = await _controller.ExportAsMarkdown(threadId);

        result.ShouldBeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task ExportAsMarkdown_NotOwner_ThrowsBusinessException()
    {
        var threadId = Guid.NewGuid();
        _threadService.Setup(s => s.IsOwnerAsync(threadId, _userId)).ReturnsAsync(false);

        await Should.ThrowAsync<BusinessException>(() => _controller.ExportAsMarkdown(threadId));
    }

    // -------------------------------------------------------------------------
    // SubmitFeedback
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SubmitFeedback_PassesCurrentUserIdToService()
    {
        var threadId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var input = new MessageFeedbackDto { Rating = true };
        _feedbackService.Setup(s => s.SubmitAsync(threadId, messageId, _userId, input))
            .ReturnsAsync(Result.Success());

        var result = await _controller.SubmitFeedback(threadId, messageId, input);

        result.Succeeded.ShouldBeTrue();
        _feedbackService.Verify(s => s.SubmitAsync(threadId, messageId, _userId, input), Times.Once);
    }

    [Fact]
    public async Task SubmitFeedback_ServiceFails_ReturnsFailedResult()
    {
        var threadId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var input = new MessageFeedbackDto { Rating = false };
        _feedbackService.Setup(s => s.SubmitAsync(threadId, messageId, _userId, input))
            .ReturnsAsync(Result.Failure("Message not found", 404));

        var result = await _controller.SubmitFeedback(threadId, messageId, input);

        result.Succeeded.ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // RevokeFeedback
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RevokeFeedback_PassesCurrentUserIdToService()
    {
        var threadId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        _feedbackService.Setup(s => s.RevokeAsync(threadId, messageId, _userId))
            .ReturnsAsync(Result.Success());

        var result = await _controller.RevokeFeedback(threadId, messageId);

        result.Succeeded.ShouldBeTrue();
        _feedbackService.Verify(s => s.RevokeAsync(threadId, messageId, _userId), Times.Once);
    }

    // -------------------------------------------------------------------------
    // Unauthenticated user
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetMyThreads_UserIdIsNull_ThrowsBusinessException()
    {
        // 模拟未认证用户（Id 为 null）
        _currentUser.Setup(u => u.Id).Returns((Guid?)null);

        var ex = await Should.ThrowAsync<BusinessException>(
            () => _controller.GetMyThreads(new ThreadListQueryDto()));
        ex.HttpStatusCode.ShouldBe(401);
    }

    [Fact]
    public async Task SubmitFeedback_UserIdIsNull_ThrowsBusinessException()
    {
        _currentUser.Setup(u => u.Id).Returns((Guid?)null);

        var ex = await Should.ThrowAsync<BusinessException>(
            () => _controller.SubmitFeedback(Guid.NewGuid(), Guid.NewGuid(), new MessageFeedbackDto { Rating = true }));
        ex.HttpStatusCode.ShouldBe(401);
    }
}
