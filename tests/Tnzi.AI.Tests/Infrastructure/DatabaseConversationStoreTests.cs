namespace Tnzi.AI.Tests.Infrastructure;

public class DatabaseConversationStoreTests
{
    [Fact]
    public async Task DeleteAsync_DelegatesToAgentThreadService()
    {
        var threadId = Guid.NewGuid();
        var thread = new AgentThread { Title = "test" };
        thread.Id = threadId;

        var internalService = new Mock<IAgentThreadInternalService>();
        var threadService = new Mock<IAgentThreadService>();
        threadService.Setup(x => x.DeleteAsync(threadId))
            .ReturnsAsync(Result.Success());

        var repository = new Mock<IRepository<AgentThread, Guid>>();
        repository.Setup(x => x.GetAsync(threadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(thread);

        var store = new DatabaseConversationStore(
            internalService.Object,
            threadService.Object,
            repository.Object,
            Mock.Of<ILogger<DatabaseConversationStore>>());

        await store.DeleteAsync(threadId.ToString());

        threadService.Verify(x => x.DeleteAsync(threadId), Times.Once);
        repository.Verify(x => x.DeleteAsync(It.IsAny<AgentThread>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
