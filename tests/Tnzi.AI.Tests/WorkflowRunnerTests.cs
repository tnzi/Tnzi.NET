namespace Tnzi.AI.Tests;

/// <summary>
/// WorkflowRunner 单元测试 — 验证步骤失败时的 partial result 行为
/// </summary>
public class WorkflowRunnerTests
{
    [Fact]
    public async Task RunSequentialAsync_AllStepsSucceed_ReturnsLastResult()
    {
        // Arrange
        var mockClient1 = CreateMockChatClient("Step 1 output");
        var mockClient2 = CreateMockChatClient("Final output");

        var agents = new List<AgentExecutor>
        {
            new(mockClient1, new AgentExecutorOptions { Name = "Agent1" }),
            new(mockClient2, new AgentExecutorOptions { Name = "Agent2" })
        };

        // Act
        var result = await WorkflowRunner.RunSequentialAsync(agents, "Hello");

        // Assert
        result.Text.ShouldBe("Final output");
        (result.FinishReason == null || !result.FinishReason.Contains("failed")).ShouldBeTrue();
    }

    [Fact]
    public async Task RunSequentialAsync_SecondStepFails_ReturnsPartialResult()
    {
        // Arrange
        var mockClient1 = CreateMockChatClient("Step 1 output");
        var failingClient = CreateFailingChatClient("Connection timeout");

        var agents = new List<AgentExecutor>
        {
            new(mockClient1, new AgentExecutorOptions { Name = "Agent1" }),
            new(failingClient, new AgentExecutorOptions { Name = "FailAgent" })
        };

        // Act
        var result = await WorkflowRunner.RunSequentialAsync(agents, "Hello");

        // Assert — should preserve Step 1's output and report failure
        result.Text.ShouldBe("Step 1 output");
        result.FinishReason.ShouldNotBeNull();
        result.FinishReason!.ShouldContain("failed");
        result.FinishReason.ShouldContain("FailAgent");
        result.FinishReason.ShouldContain("Connection timeout");
        result.FinishReason.ShouldContain("Step 2/2");
    }

    [Fact]
    public async Task RunSequentialAsync_FirstStepFails_ReturnsInputAsText()
    {
        // Arrange
        var failingClient = CreateFailingChatClient("API error");

        var agents = new List<AgentExecutor>
        {
            new(failingClient, new AgentExecutorOptions { Name = "FailAgent" })
        };

        // Act
        var result = await WorkflowRunner.RunSequentialAsync(agents, "Initial input");

        // Assert — should preserve the original input
        result.Text.ShouldBe("Initial input");
        result.FinishReason.ShouldNotBeNull();
        result.FinishReason!.ShouldContain("Step 1/1");
        result.FinishReason.ShouldContain("failed");
    }

    [Fact]
    public async Task RunParallelAsync_AllSucceed_CombinesResults()
    {
        // Arrange
        var mockClient1 = CreateMockChatClient("Result A");
        var mockClient2 = CreateMockChatClient("Result B");

        var agents = new List<AgentExecutor>
        {
            new(mockClient1, new AgentExecutorOptions { Name = "AgentA" }),
            new(mockClient2, new AgentExecutorOptions { Name = "AgentB" })
        };

        // Act
        var result = await WorkflowRunner.RunParallelAsync(agents, "Hello");

        // Assert
        var text = result.Text!;
        text.ShouldContain("[AgentA]");
        text.ShouldContain("Result A");
        text.ShouldContain("[AgentB]");
        text.ShouldContain("Result B");
    }

    /// <summary>
    /// 创建返回固定文本的 Mock IChatClient
    /// </summary>
    private static IChatClient CreateMockChatClient(string responseText)
    {
        var mock = new Mock<IChatClient>();
        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));
        return mock.Object;
    }

    /// <summary>
    /// 创建抛出异常的 Mock IChatClient
    /// </summary>
    private static IChatClient CreateFailingChatClient(string errorMessage)
    {
        var mock = new Mock<IChatClient>();
        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(errorMessage));
        return mock.Object;
    }
}
