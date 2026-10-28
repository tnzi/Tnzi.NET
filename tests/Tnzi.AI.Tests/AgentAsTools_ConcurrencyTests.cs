namespace Tnzi.AI.Tests;

public class AgentAsTools_ConcurrencyTests
{
    [Fact]
    public void SubAgentTimeout_DefaultIs15Minutes()
    {
        var config = new AgentAsToolsConfiguration
        {
            Agents = new() { ["a"] = Guid.NewGuid() }
        };
        Assert.Equal(TimeSpan.FromMinutes(15), config.SubAgentTimeout);
    }

    [Fact]
    public void SubAgentEventTypes_HasAllConstants()
    {
        Assert.Equal("sub_agent_started", SubAgentEventTypes.Started);
        Assert.Equal("sub_agent_completed", SubAgentEventTypes.Completed);
        Assert.Equal("sub_agent_failed", SubAgentEventTypes.Failed);
        Assert.Equal("sub_agent_timed_out", SubAgentEventTypes.TimedOut);
    }

    [Fact]
    public async Task TwoChildCallsInOneResponse_RunOneAfterAnother()
    {
        // call_* 不在工具注册表里，执行器按未知工具失败关闭、顺序执行。文档与策略都按「顺序」描述它：
        // 这条用例钉住这一点，哪天改成并行时必须同时补上每个子 Agent 自己的作用域与属性包
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var running = 0;
        var maxRunning = 0;
        var gate = new object();

        IAgentExecutor SlowChild(string name)
        {
            var child = new Mock<IAgentExecutor>();
            child.SetupGet(c => c.Name).Returns(name);
            child.Setup(c => c.ExecuteAsync(It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    lock (gate) { maxRunning = Math.Max(maxRunning, ++running); }
                    await Task.Delay(100);
                    lock (gate) { running--; }
                    return new AgentResponse { Text = $"{name} done" };
                });
            return child.Object;
        }

        var parentCalls = 0;
        var client = new Mock<IChatClient>();
        client.Setup(x => x.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++parentCalls == 1
                ? new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [
                    new FunctionCallContent("c1", "call_first", new Dictionary<string, object?> { ["task"] = "one" }),
                    new FunctionCallContent("c2", "call_second", new Dictionary<string, object?> { ["task"] = "two" })
                ]))
                : new ChatResponse(new ChatMessage(ChatRole.Assistant, "all done")));
        var parent = new AgentExecutor(client.Object, new AgentExecutorOptions { Name = "orchestrator" });

        var repository = new Mock<IRepository<Agent, Guid>>();
        repository.Setup(x => x.GetAsync(firstId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent { Id = firstId, Name = "first", Provider = "test", IsEnabled = true });
        repository.Setup(x => x.GetAsync(secondId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent { Id = secondId, Name = "second", Provider = "test", IsEnabled = true });

        var factory = new Mock<IAgentFactory>();
        foreach (var (id, name) in new[] { (firstId, "first"), (secondId, "second") })
        {
            var child = SlowChild(name);
            factory.Setup(x => x.CreateAgentAsync(
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), name,
                    It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(), It.IsAny<int?>(),
                    It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(),
                    id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(child);
        }

        var context = new ExecutionStrategyContext
        {
            AgentFactory = factory.Object,
            AgentRepository = repository.Object,
            ServiceProvider = TestHelpers.ServiceProviderWithGrants(),
            ExecutionContextAccessor = new AgentExecutionContextAccessor(),
            Logger = Mock.Of<ILogger>()
        };
        var strategy = new AgentAsToolsExecutionStrategy(new AgentAsToolsConfiguration
        {
            Agents = new Dictionary<string, Guid> { ["first"] = firstId, ["second"] = secondId }
        });

        var result = await strategy.ExecuteAsync(parent, [new ChatMessage(ChatRole.User, "go")], context, CancellationToken.None);

        result.Response.Text.ShouldBe("all done");
        factory.Invocations.Count.ShouldBe(2);
        maxRunning.ShouldBe(1);
    }
}
