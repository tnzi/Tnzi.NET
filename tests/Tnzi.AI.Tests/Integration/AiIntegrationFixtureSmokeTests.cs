namespace Tnzi.AI.Tests.Integration;

/// <summary>
/// 集成测试 Fixture 冒烟测试 - 验证 MockChatClientProvider、AiIntegrationTestBase、StreamingTestHelper 基本工作
/// </summary>
public class AiIntegrationFixtureSmokeTests : AiIntegrationTestBase
{
    [Fact]
    public async Task RunAsync_WithQueuedTextResponse_ReturnsExpectedText()
    {
        // Arrange
        MockProvider.EnqueueResponse("Hello, I am a mock AI assistant.");
        var request = CreateRequest("Hi there");

        // Act
        var result = await Runtime.RunAsync(request);

        // Assert
        result.ShouldNotBeNull();
        result.Response.ShouldContain("Hello, I am a mock AI assistant.");
    }

    [Fact]
    public async Task RunStreamingAsync_WithQueuedTextResponse_StreamsCharByChar()
    {
        // Arrange
        MockProvider.EnqueueResponse("Hello!");
        var request = CreateRequest("Hi");

        // Act
        var stream = Runtime.RunStreamingAsync(request);
        var result = await StreamingTestHelper.CollectStreamAsync(stream);

        // Assert
        result.FullText.ShouldContain("Hello!");
        result.ChunkCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task CreateAgentAsync_PersistsToDatabase()
    {
        // Act
        var agent = await CreateAgentAsync("TestBot", "You are a test bot.");

        // Assert
        agent.Id.ShouldNotBe(Guid.Empty);
        var fromDb = await DbContext.Set<Agent>().FindAsync(agent.Id);
        fromDb.ShouldNotBeNull();
        fromDb.Name.ShouldBe("TestBot");
        fromDb.Instructions.ShouldBe("You are a test bot.");
    }

    [Fact]
    public async Task CreateThreadAsync_PersistsToDatabase()
    {
        // Arrange
        var agent = await CreateAgentAsync("ThreadBot");

        // Act
        var thread = await CreateThreadAsync(agent.Id);

        // Assert
        thread.Id.ShouldNotBe(Guid.Empty);
        var fromDb = await DbContext.Set<AgentThread>().FindAsync(thread.Id);
        fromDb.ShouldNotBeNull();
        fromDb.AgentId.ShouldBe(agent.Id);
    }

    [Fact]
    public void MockProvider_EnqueueAndDequeue_WorksCorrectly()
    {
        // Arrange
        MockProvider.EnqueueResponse("First");
        MockProvider.EnqueueResponse("Second");

        // Assert
        MockProvider.PendingResponseCount.ShouldBe(2);
    }

    [Fact]
    public async Task MockProvider_MultipleResponses_DequeueInOrder()
    {
        // Arrange
        MockProvider.EnqueueResponse("Response 1");
        MockProvider.EnqueueResponse("Response 2");

        // Act
        var result1 = await Runtime.RunAsync(CreateRequest("Message 1"));
        var result2 = await Runtime.RunAsync(CreateRequest("Message 2"));

        // Assert
        result1.Response.ShouldContain("Response 1");
        result2.Response.ShouldContain("Response 2");
    }

    [Fact]
    public async Task StreamingTestHelper_CollectStream_CapturesAllFields()
    {
        // Arrange
        MockProvider.EnqueueResponse("Test streaming output");
        var request = CreateRequest("Test");

        // Act
        var stream = Runtime.RunStreamingAsync(request);
        var result = await StreamingTestHelper.CollectStreamAsync(stream);

        // Assert
        result.ShouldNotBeNull();
        result.FullText.ShouldNotBeNullOrWhiteSpace();
        result.AllChunks.ShouldNotBeEmpty();
        result.HasErrors.ShouldBeFalse();
    }
}
