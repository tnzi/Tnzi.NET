namespace Tnzi.AI.Tests;

/// <summary>
/// GroupChatOrchestrator 单元测试 — 验证多 Agent 讨论编排
/// </summary>
public class GroupChatOrchestratorTests
{
    [Fact]
    public async Task RunAsync_RoundRobin_CyclesThroughAgents()
    {
        var orchestrator = new GroupChatOrchestrator(Mock.Of<ILogger<GroupChatOrchestrator>>());
        orchestrator.AddAgent(CreateAgent("AgentA", "Thought A"));
        orchestrator.AddAgent(CreateAgent("AgentB", "Thought B"));
        orchestrator.Options = new GroupChatOptions
        {
            MaxRounds = 2,
            SelectionStrategy = GroupChatSelectionStrategy.RoundRobin,
            TerminationKeywords = [] // No early termination
        };

        var result = await orchestrator.RunAsync("Discuss this topic");

        result.TotalRounds.ShouldBe(2);
        result.History.Count.ShouldBeGreaterThanOrEqualTo(2);
        result.Output.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RunAsync_TerminationKeyword_StopsEarly()
    {
        var orchestrator = new GroupChatOrchestrator(Mock.Of<ILogger<GroupChatOrchestrator>>());
        orchestrator.AddAgent(CreateAgent("AgentA", "I think we should TERMINATE"));
        orchestrator.AddAgent(CreateAgent("AgentB", "I agree"));
        orchestrator.Options = new GroupChatOptions
        {
            MaxRounds = 10,
            SelectionStrategy = GroupChatSelectionStrategy.RoundRobin,
            TerminationKeywords = ["TERMINATE"]
        };

        var result = await orchestrator.RunAsync("Discuss this topic");

        // Should terminate after first agent says TERMINATE
        result.TotalRounds.ShouldBeLessThan(10);
    }

    [Fact]
    public async Task RunAsync_MaxRounds_Reached()
    {
        var orchestrator = new GroupChatOrchestrator(Mock.Of<ILogger<GroupChatOrchestrator>>());
        orchestrator.AddAgent(CreateAgent("AgentA", "Thought A"));
        orchestrator.AddAgent(CreateAgent("AgentB", "Thought B"));
        orchestrator.Options = new GroupChatOptions
        {
            MaxRounds = 3,
            SelectionStrategy = GroupChatSelectionStrategy.RoundRobin,
            TerminationKeywords = [] // No early termination
        };

        var result = await orchestrator.RunAsync("Topic");

        result.TotalRounds.ShouldBe(3);
    }

    [Fact]
    public async Task RunAsync_SingleAgent_ThrowsInvalidOperation()
    {
        var orchestrator = new GroupChatOrchestrator(Mock.Of<ILogger<GroupChatOrchestrator>>());
        orchestrator.AddAgent(CreateAgent("OnlyAgent", "My response DONE"));

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await orchestrator.RunAsync("Solo topic"));
    }

    [Fact]
    public async Task RunAsync_CustomTermination_StopsCorrectly()
    {
        var orchestrator = new GroupChatOrchestrator(Mock.Of<ILogger<GroupChatOrchestrator>>());
        orchestrator.AddAgent(CreateAgent("AgentA", "Round response"));
        orchestrator.AddAgent(CreateAgent("AgentB", "Another response"));
        orchestrator.Options = new GroupChatOptions
        {
            MaxRounds = 100,
            TerminationKeywords = [],
            TerminationCondition = history => history.Count >= 2
        };

        var result = await orchestrator.RunAsync("Topic");

        result.History.Count.ShouldBeGreaterThanOrEqualTo(2);
        result.TotalRounds.ShouldBeLessThan(100);
    }

    /// <summary>
    /// 创建返回固定文本的 AgentExecutor
    /// </summary>
    private static AgentExecutor CreateAgent(string name, string response)
    {
        var mock = new Mock<IChatClient>();
        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));
        return new AgentExecutor(mock.Object, new AgentExecutorOptions { Name = name });
    }
}
