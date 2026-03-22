using AgentThreadEntity = Tnzi.AI.Entities.AgentThread;

namespace Tnzi.AI.Tests;

public class ThreadTitleGenerationHandlerTests
{
    private static IOptionsMonitor<ThreadOptions> CreateOptions(bool autoGenerateTitle, int titleMaxLength = 50)
    {
        var options = new ThreadOptions
        {
            AutoGenerateTitle = autoGenerateTitle,
            TitleMaxLength = titleMaxLength
        };
        var monitor = new Mock<IOptionsMonitor<ThreadOptions>>();
        monitor.Setup(x => x.CurrentValue).Returns(options);
        return monitor.Object;
    }

    [Fact]
    public async Task HandleAsync_WhenEnabled_GeneratesAndUpdatesTitle()
    {
        var threadId = Guid.NewGuid();
        var thread = new AgentThreadEntity { Title = null };
        thread.Id = threadId;

        var aiUtility = new Mock<IAiUtility>();
        aiUtility.Setup(x => x.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Code Help");

        var repository = new Mock<IRepository<AgentThreadEntity, Guid>>();
        repository.Setup(x => x.GetAsync(threadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(thread);
        repository.Setup(x => x.UpdateAsync(It.IsAny<AgentThreadEntity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new ThreadTitleGenerationHandler(
            aiUtility.Object, repository.Object, CreateOptions(true));

        var @event = new ThreadFirstReplyCompletedEvent
        {
            ThreadId = threadId,
            UserMessage = "How do I write a loop?",
            AssistantReply = "You can use a for loop."
        };

        await handler.HandleAsync(@event);

        thread.Title.ShouldBe("Code Help");
        repository.Verify(x => x.UpdateAsync(thread, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenDisabled_DoesNotCallUtility()
    {
        var aiUtility = new Mock<IAiUtility>();
        var repository = new Mock<IRepository<AgentThreadEntity, Guid>>();

        var handler = new ThreadTitleGenerationHandler(
            aiUtility.Object, repository.Object, CreateOptions(false));

        var @event = new ThreadFirstReplyCompletedEvent
        {
            ThreadId = Guid.NewGuid(),
            UserMessage = "Hello",
            AssistantReply = "Hi"
        };

        await handler.HandleAsync(@event);

        aiUtility.Verify(x => x.ExecuteAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(x => x.UpdateAsync(It.IsAny<AgentThreadEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenUtilityReturnsNull_DoesNotUpdateTitle()
    {
        var threadId = Guid.NewGuid();
        var thread = new AgentThreadEntity { Title = null };
        thread.Id = threadId;

        var aiUtility = new Mock<IAiUtility>();
        aiUtility.Setup(x => x.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<AiUtilityCallOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var repository = new Mock<IRepository<AgentThreadEntity, Guid>>();
        repository.Setup(x => x.GetAsync(threadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(thread);

        var handler = new ThreadTitleGenerationHandler(
            aiUtility.Object, repository.Object, CreateOptions(true));

        var @event = new ThreadFirstReplyCompletedEvent
        {
            ThreadId = threadId,
            UserMessage = "Hello",
            AssistantReply = "Hi"
        };

        await handler.HandleAsync(@event);

        repository.Verify(x => x.UpdateAsync(It.IsAny<AgentThreadEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenExceptionThrown_DoesNotRethrow()
    {
        var threadId = Guid.NewGuid();

        var aiUtility = new Mock<IAiUtility>();
        var repository = new Mock<IRepository<AgentThreadEntity, Guid>>();
        repository.Setup(x => x.GetAsync(threadId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        var handler = new ThreadTitleGenerationHandler(
            aiUtility.Object, repository.Object, CreateOptions(true));

        var @event = new ThreadFirstReplyCompletedEvent
        {
            ThreadId = threadId,
            UserMessage = "Hello",
            AssistantReply = "Hi"
        };

        // Should not throw
        var ex = await Record.ExceptionAsync(() => handler.HandleAsync(@event));
        ex.ShouldBeNull();
    }
}
