using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Tnzi.AI.Controllers;
using Tnzi.Exceptions;

namespace Tnzi.AI.Tests.Controllers;

/// <summary>
/// The endpoints a chat client uses around a turn that outlives its request (find the run, cancel it), and to show a
/// conversation (page messages back, read an attachment).
/// </summary>
public class ChatRunAndAttachmentEndpointTests
{
    private readonly Guid _userId = Guid.NewGuid();

    [Fact]
    public async Task ActiveRuns_AreAskedForAsTheSignedInUser()
    {
        var chat = new Mock<IChatService>();
        var threadId = Guid.NewGuid();
        chat.Setup(s => s.GetActiveRunsAsync(threadId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<ChatRunDto>>.Success([new ChatRunDto { RunId = Guid.NewGuid() }]));
        var controller = WithUser(new DefaultChatController(chat.Object));

        var result = await controller.GetActiveRuns(threadId);

        result.Succeeded.ShouldBeTrue();
        result.Data!.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task CancelRun_CancelsOnTheServer()
    {
        var chat = new Mock<IChatService>();
        var runId = Guid.NewGuid();
        chat.Setup(s => s.CancelRunAsync(runId, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        var controller = WithUser(new DefaultChatController(chat.Object));

        (await controller.CancelRun(runId)).Succeeded.ShouldBeTrue();
        chat.Verify(s => s.CancelRunAsync(runId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Messages_ArePagedForTheThreadsOwnerOnly()
    {
        var threads = new Mock<IAgentThreadService>();
        var threadId = Guid.NewGuid();
        threads.Setup(t => t.IsOwnerAsync(threadId, _userId)).ReturnsAsync(true);
        threads.Setup(t => t.GetMessagesAsync(threadId, 40, 20))
            .ReturnsAsync(Result<ThreadMessagePageDto>.Success(new ThreadMessagePageDto { HasMore = true }));
        var controller = WithUser(new DefaultThreadController(threads.Object, Mock.Of<IMessageFeedbackService>()));

        var page = await controller.GetMessages(threadId, beforeOrder: 40, limit: 20);

        page.Data!.HasMore.ShouldBeTrue();
        await Should.ThrowAsync<BusinessException>(() => controller.GetMessages(Guid.NewGuid()));
    }

    [Fact]
    public async Task Attachment_IsServedWithItsTypeAndName_AndBrowsersAreToldNotToGuess()
    {
        var threadId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var attachments = new Mock<IAgentAttachmentService>();
        attachments.Setup(a => a.GetContentAsync(threadId, attachmentId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AttachmentContentDto>.Success(new AttachmentContentDto { MediaType = "image/png", FileName = "a.png", Content = [1, 2] }));
        var controller = WithUser(new DefaultThreadController(Mock.Of<IAgentThreadService>(), Mock.Of<IMessageFeedbackService>(), attachmentService: attachments.Object));

        var result = await controller.GetAttachment(threadId, attachmentId);

        var file = result.ShouldBeOfType<FileContentResult>();
        file.ContentType.ShouldBe("image/png");
        file.FileDownloadName.ShouldBe("a.png");
        file.FileContents.ShouldBe(new byte[] { 1, 2 });
        controller.Response.Headers["X-Content-Type-Options"].ToString().ShouldBe("nosniff");
    }

    [Fact]
    public async Task Attachment_ThatIsNotTheCallers_IsNotFound()
    {
        var attachments = new Mock<IAgentAttachmentService>();
        attachments.Setup(a => a.GetContentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AttachmentContentDto>.Failure("Attachment not found.", 404, ErrorCodes.AttachmentNotFound));
        var controller = WithUser(new DefaultThreadController(Mock.Of<IAgentThreadService>(), Mock.Of<IMessageFeedbackService>(), attachmentService: attachments.Object));

        (await controller.GetAttachment(Guid.NewGuid(), Guid.NewGuid())).ShouldBeOfType<NotFoundObjectResult>();
    }

    private T WithUser<T>(T controller) where T : ControllerBase
    {
        var user = new Mock<ICurrentUser>();
        user.Setup(u => u.Id).Returns(_userId);
        user.Setup(u => u.IsAuthenticated).Returns(true);

        var services = new Mock<IServiceProvider>();
        var loggers = new Mock<ILoggerFactory>();
        loggers.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        services.Setup(sp => sp.GetService(typeof(ILoggerFactory))).Returns(loggers.Object);
        services.Setup(sp => sp.GetService(typeof(ICurrentUser))).Returns(user.Object);

        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = services.Object } };
        return controller;
    }
}
