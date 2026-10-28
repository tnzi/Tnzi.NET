namespace Tnzi.AI.Tests.Engine;

/// <summary>
/// AgentAsTools 的子 Agent 标记只属于子 Agent：子 Agent 返回后，父 Agent 后续的工具调用不能按子 Agent 的身份评估。
/// </summary>
/// <remarks>
/// 运行的属性包是整次运行共享的同一个实例（<c>AgentRuntime</c> 进入运行时就建好）。<c>call_*</c> 若直接往里写
/// IsSubAgent / SubAgentName，结束时的还原只换掉自己异步帧里的引用，共享实例上的标记留了下来：
/// 父 Agent 之后的工具按「子 Agent、名叫 Researcher」匹配权限规则（IsSubAgentOnly 的规则误伤父 Agent，
/// 按子 Agent 名放行的规则放行了父 Agent）。这里先在测试的执行流里建好属性包，与运行时同形。
/// </remarks>
public class AgentAsToolsPropertyIsolationTests
{
    [Fact]
    public async Task ParentToolAfterAChildCall_IsNotEvaluatedAsTheChild()
    {
        var accessor = new AgentExecutionContextAccessor();
        accessor.RestoreProperties(new Dictionary<string, object>());

        var childId = Guid.NewGuid();
        bool? childSawSubAgentMark = null;
        var strategy = new AgentAsToolsExecutionStrategy(new AgentAsToolsConfiguration
        {
            Agents = new Dictionary<string, Guid> { ["Researcher"] = childId }
        });

        var parentProbeSaw = new Dictionary<string, object>();
        var probe = AIFunctionFactory.Create(() =>
        {
            foreach (var (key, value) in accessor.Properties)
            {
                parentProbeSaw[key] = value;
            }
            return "probed";
        }, "parent_probe");

        var parent = new AgentExecutor(ParentClient(), new AgentExecutorOptions { Name = "Lead", Tools = [probe] });

        var repository = new Mock<IRepository<Agent, Guid>>();
        repository.Setup(x => x.GetAsync(childId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent { Id = childId, Name = "Researcher", Provider = "test", IsEnabled = true });
        var agentFactory = new Mock<IAgentFactory>();
        agentFactory.Setup(x => x.CreateAgentAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), "Researcher",
                It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(), It.IsAny<int?>(),
                It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(),
                childId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                childSawSubAgentMark = accessor.Properties.TryGetValue(ContextPropertyKeys.IsSubAgent, out var flag) && flag is true;
                return TextAgent("Researcher", "child findings");
            });

        var context = new ExecutionStrategyContext
        {
            AgentFactory = agentFactory.Object,
            AgentRepository = repository.Object,
            ServiceProvider = TestHelpers.ServiceProviderWithGrants(),
            ExecutionContextAccessor = accessor,
            Logger = Mock.Of<ILogger>()
        };

        await strategy.ExecuteAsync(parent, [new ChatMessage(ChatRole.User, "go")], context, CancellationToken.None);

        childSawSubAgentMark.ShouldBe(true, "the child itself runs as a sub-agent");
        parentProbeSaw.ShouldNotContainKey(ContextPropertyKeys.IsSubAgent);
        parentProbeSaw.ShouldNotContainKey(ContextPropertyKeys.SubAgentName);
        accessor.Properties.ShouldNotContainKey(ContextPropertyKeys.IsSubAgent);
    }

    /// <summary>第一轮调子 Agent，第二轮调父 Agent 自己的探针工具，第三轮收尾。</summary>
    private static IChatClient ParentClient()
    {
        var calls = 0;
        var client = new Mock<IChatClient>();
        client.Setup(x => x.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++calls switch
            {
                1 => new ChatResponse([new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", "call_researcher", new Dictionary<string, object?> { ["task"] = "dig" })])]),
                2 => new ChatResponse([new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c2", "parent_probe")])]),
                _ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "done"))
            });
        return client.Object;
    }

    private static AgentExecutor TextAgent(string name, string text)
    {
        var client = new Mock<IChatClient>();
        client.Setup(x => x.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
        return new AgentExecutor(client.Object, new AgentExecutorOptions { Name = name });
    }
}
