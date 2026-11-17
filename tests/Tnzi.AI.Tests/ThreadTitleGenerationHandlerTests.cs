namespace Tnzi.AI.Tests;

public class ThreadTitleGenerationHandlerTests
{
    /// <summary>
    /// The AI title is requested the moment the first message is accepted, while the reply is still being written.
    /// A synchronous handler would make the publisher (the turn itself) wait for a second model call before it can
    /// even start answering; as a background handler the turn and the title run side by side.
    /// </summary>
    [Fact]
    public void Handler_RunsInTheBackground()
    {
        var metadata = EventHandlerInvoker.GetMetadata(typeof(ThreadTitleGenerationHandler), typeof(ThreadTitleRequestedEvent));

        metadata.IsBackground.ShouldBeTrue();
    }

    [Fact]
    public async Task HandleAsync_GeneratesTheTitleInItsOwnScope()
    {
        var titles = new Mock<IThreadTitleService>();
        var handler = new ThreadTitleGenerationHandler(ScopeFactory(titles.Object));
        var @event = new ThreadTitleRequestedEvent { ThreadId = Guid.NewGuid(), UserMessage = "plan a trip" };

        await handler.HandleAsync(@event);

        titles.Verify(t => t.GenerateAsync(@event, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithoutATitleService_DoesNothing()
    {
        var handler = new ThreadTitleGenerationHandler(ScopeFactory(null));

        await Should.NotThrowAsync(() => handler.HandleAsync(new ThreadTitleRequestedEvent { ThreadId = Guid.NewGuid() }));
    }

    /// <summary>
    /// A failed write is left to the bus (retry, dead letter): swallowing it here would turn every storage failure
    /// into a thread that silently keeps its fallback title.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WriteFailure_Propagates()
    {
        var titles = new Mock<IThreadTitleService>();
        titles.Setup(t => t.GenerateAsync(It.IsAny<ThreadTitleRequestedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));
        var handler = new ThreadTitleGenerationHandler(ScopeFactory(titles.Object));

        await Should.ThrowAsync<InvalidOperationException>(
            () => handler.HandleAsync(new ThreadTitleRequestedEvent { ThreadId = Guid.NewGuid(), UserMessage = "x" }));
    }

    private static IServiceScopeFactory ScopeFactory(IThreadTitleService? titles)
    {
        var services = new ServiceCollection();
        if (titles is not null)
        {
            services.AddScoped(_ => titles);
        }

        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }
}
