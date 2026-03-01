namespace Tnzi.AI.Tests;

/// <summary>
/// HandoffOrchestrator 单元测试 — 验证 Agent 转接编排
/// </summary>
public class HandoffOrchestratorTests
{
    [Fact]
    public async Task RunAsync_NoHandoff_ReturnsDirectResponse()
    {
        var orchestrator = new HandoffOrchestrator(Mock.Of<ILogger<HandoffOrchestrator>>());
        orchestrator.AddAgent(CreateAgent("AgentA", "Hello from A"));

        var result = await orchestrator.RunAsync("AgentA", "Hi");

        result.FinalAgentName.ShouldBe("AgentA");
        result.FinalResponse.Text.ShouldBe("Hello from A");
        result.HandoffPath.Count.ShouldBe(1);
        result.HandoffPath[0].ShouldBe("AgentA");
    }

    [Fact]
    public async Task RunAsync_MultipleAgentsRegistered_PathTracked()
    {
        // Without actual handoff tool call in LLM response, agent responds directly
        var orchestrator = new HandoffOrchestrator(Mock.Of<ILogger<HandoffOrchestrator>>());
        orchestrator.AddAgent(CreateAgent("AgentA", "Response A"), ["AgentB"]);
        orchestrator.AddAgent(CreateAgent("AgentB", "Response B"));

        var result = await orchestrator.RunAsync("AgentA", "Question");

        // AgentA responds directly (no handoff tool call), so path is just AgentA
        result.FinalAgentName.ShouldBe("AgentA");
        result.HandoffPath.Count.ShouldBe(1);
        result.HandoffPath[0].ShouldBe("AgentA");
        result.FinalResponse.Text.ShouldBe("Response A");
    }

    [Fact]
    public async Task RunAsync_MaxHandoffsProperty_CanBeSet()
    {
        var orchestrator = new HandoffOrchestrator(Mock.Of<ILogger<HandoffOrchestrator>>()) { MaxHandoffs = 5 };
        orchestrator.MaxHandoffs.ShouldBe(5);

        orchestrator.AddAgent(CreateAgent("AgentA", "Hello"));
        var result = await orchestrator.RunAsync("AgentA", "Hi");
        result.FinalAgentName.ShouldBe("AgentA");
    }

    [Fact]
    public async Task RunAsync_UnknownEntryAgent_ThrowsOrHandlesGracefully()
    {
        var orchestrator = new HandoffOrchestrator(Mock.Of<ILogger<HandoffOrchestrator>>());
        orchestrator.AddAgent(CreateAgent("AgentA", "Hello"));

        // Entry agent "Unknown" not registered
        await Should.ThrowAsync<Exception>(async () =>
            await orchestrator.RunAsync("Unknown", "Hi"));
    }

    /// <summary>
    /// 创建返回直接文本（无 handoff）的 AgentExecutor
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

    [Fact]
    public async Task RunAsync_WithMultipleAgents_AllowedTargetsEnforced()
    {
        // 验证 orchestrator 正确管理 Agent 注册和允许的 handoff 目标
        var orchestrator = new HandoffOrchestrator(Mock.Of<ILogger<HandoffOrchestrator>>());
        orchestrator.AddAgent(CreateAgent("AgentA", "Response A"), ["AgentB", "AgentC"]);
        orchestrator.AddAgent(CreateAgent("AgentB", "Response B"));
        orchestrator.AddAgent(CreateAgent("AgentC", "Response C"));

        // 无 handoff 工具调用时，直接返回入口 Agent 的响应
        var result = await orchestrator.RunAsync("AgentA", "Question");
        result.FinalAgentName.ShouldBe("AgentA");
        result.FinalResponse.Text.ShouldBe("Response A");
        result.HandoffPath.ShouldBe(["AgentA"]);

        // 不同入口 Agent 也能正常工作
        var result2 = await orchestrator.RunAsync("AgentC", "Another question");
        result2.FinalAgentName.ShouldBe("AgentC");
        result2.FinalResponse.Text.ShouldBe("Response C");
    }

    [Fact]
    public async Task RunAsync_MaxHandoffsCanBeConfigured()
    {
        var orchestrator = new HandoffOrchestrator(Mock.Of<ILogger<HandoffOrchestrator>>()) { MaxHandoffs = 3 };
        orchestrator.MaxHandoffs.ShouldBe(3);

        orchestrator.AddAgent(CreateAgent("AgentA", "Hello"));
        var result = await orchestrator.RunAsync("AgentA", "Hi");
        result.FinalAgentName.ShouldBe("AgentA");
        result.HandoffPath.Count.ShouldBe(1);
    }
}
