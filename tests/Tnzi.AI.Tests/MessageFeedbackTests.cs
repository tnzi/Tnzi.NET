using AgentThreadEntity = Tnzi.AI.Entities.AgentThread;

namespace Tnzi.AI.Tests;

/// <summary>
/// 消息级反馈服务测试
/// </summary>
public class MessageFeedbackTests
{
    private MessageFeedbackService CreateService(
        Mock<IRepository<AgentThreadEntity, Guid>> threadRepo,
        Mock<IRepository<AgentThreadMessage, Guid>> messageRepo)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return new MessageFeedbackService(
            services.BuildServiceProvider(),
            messageRepo.Object,
            threadRepo.Object);
    }

    private static (Mock<IRepository<AgentThreadEntity, Guid>>, Mock<IRepository<AgentThreadMessage, Guid>>, Guid, Guid, Guid)
        SetupOwned(string role = "assistant", bool hasFeedback = false)
    {
        var userId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        var thread = new AgentThreadEntity { Id = threadId, CreatorId = userId };
        var message = new AgentThreadMessage
        {
            Id = messageId,
            ThreadId = threadId,
            Role = role,
            Content = "Hello",
            FeedbackRating = hasFeedback ? (bool?)true : null
        };

        var threadRepo = new Mock<IRepository<AgentThreadEntity, Guid>>();
        threadRepo.Setup(x => x.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<AgentThreadEntity, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(thread);

        var messageRepo = new Mock<IRepository<AgentThreadMessage, Guid>>();
        messageRepo.Setup(x => x.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<AgentThreadMessage, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(message);
        messageRepo.Setup(x => x.UpdateAsync(It.IsAny<AgentThreadMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return (threadRepo, messageRepo, userId, threadId, messageId);
    }

    [Fact]
    public async Task SubmitAsync_ThumbsUp_Succeeds()
    {
        var (threadRepo, messageRepo, userId, threadId, messageId) = SetupOwned();
        var service = CreateService(threadRepo, messageRepo);

        var input = new MessageFeedbackDto { Rating = true, Comment = "Great answer!" };
        var result = await service.SubmitAsync(threadId, messageId, userId, input);

        result.Succeeded.ShouldBeTrue();
        messageRepo.Verify(x => x.UpdateAsync(
            It.Is<AgentThreadMessage>(m => m.FeedbackRating == true && m.FeedbackTime != null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_ThumbsDownWithTags_Succeeds()
    {
        var (threadRepo, messageRepo, userId, threadId, messageId) = SetupOwned();
        var service = CreateService(threadRepo, messageRepo);

        var tags = new List<string> { FeedbackTags.Incorrect, FeedbackTags.TooLong };
        var input = new MessageFeedbackDto { Rating = false, Tags = tags };
        var result = await service.SubmitAsync(threadId, messageId, userId, input);

        result.Succeeded.ShouldBeTrue();
        messageRepo.Verify(x => x.UpdateAsync(
            It.Is<AgentThreadMessage>(m =>
                m.FeedbackRating == false &&
                m.FeedbackTags != null &&
                m.FeedbackTags.Contains("incorrect")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_MessageNotFound_Returns404()
    {
        var userId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        var thread = new AgentThreadEntity { Id = threadId, CreatorId = userId };
        var threadRepo = new Mock<IRepository<AgentThreadEntity, Guid>>();
        threadRepo.Setup(x => x.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<AgentThreadEntity, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(thread);

        var messageRepo = new Mock<IRepository<AgentThreadMessage, Guid>>();
        messageRepo.Setup(x => x.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<AgentThreadMessage, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentThreadMessage?)null);

        var service = CreateService(threadRepo, messageRepo);
        var result = await service.SubmitAsync(threadId, messageId, userId, new MessageFeedbackDto { Rating = true });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
        result.ErrorCode.ShouldBe(ErrorCodes.MessageNotFound);
    }

    [Fact]
    public async Task SubmitAsync_NonAssistantMessage_Returns400()
    {
        var (threadRepo, messageRepo, userId, threadId, messageId) = SetupOwned(role: "user");
        var service = CreateService(threadRepo, messageRepo);

        var result = await service.SubmitAsync(threadId, messageId, userId, new MessageFeedbackDto { Rating = true });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.ErrorCode.ShouldBe(ErrorCodes.FeedbackOnlyAssistant);
    }

    [Fact]
    public async Task SubmitAsync_NotOwner_Returns404()
    {
        var threadId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var differentUserId = Guid.NewGuid();

        var threadRepo = new Mock<IRepository<AgentThreadEntity, Guid>>();
        threadRepo.Setup(x => x.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<AgentThreadEntity, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentThreadEntity?)null);

        var messageRepo = new Mock<IRepository<AgentThreadMessage, Guid>>();

        var service = CreateService(threadRepo, messageRepo);
        var result = await service.SubmitAsync(threadId, messageId, differentUserId, new MessageFeedbackDto { Rating = true });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task SubmitAsync_AlreadyRated_UpdatesFeedback()
    {
        var (threadRepo, messageRepo, userId, threadId, messageId) = SetupOwned(hasFeedback: true);
        var service = CreateService(threadRepo, messageRepo);

        // 已有 true 反馈，改为 false（应该允许覆盖）
        var input = new MessageFeedbackDto { Rating = false, Comment = "Changed my mind" };
        var result = await service.SubmitAsync(threadId, messageId, userId, input);

        result.Succeeded.ShouldBeTrue();
        messageRepo.Verify(x => x.UpdateAsync(
            It.Is<AgentThreadMessage>(m => m.FeedbackRating == false),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokeAsync_ExistingFeedback_ClearsFields()
    {
        var (threadRepo, messageRepo, userId, threadId, messageId) = SetupOwned(hasFeedback: true);
        var service = CreateService(threadRepo, messageRepo);

        var result = await service.RevokeAsync(threadId, messageId, userId);

        result.Succeeded.ShouldBeTrue();
        messageRepo.Verify(x => x.UpdateAsync(
            It.Is<AgentThreadMessage>(m =>
                m.FeedbackRating == null &&
                m.FeedbackTags == null &&
                m.FeedbackComment == null &&
                m.FeedbackTime == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokeAsync_NoFeedback_Returns404()
    {
        // hasFeedback=false → FeedbackRating is null → no feedback to revoke
        var (threadRepo, messageRepo, userId, threadId, messageId) = SetupOwned(hasFeedback: false);
        var service = CreateService(threadRepo, messageRepo);

        var result = await service.RevokeAsync(threadId, messageId, userId);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }
}
