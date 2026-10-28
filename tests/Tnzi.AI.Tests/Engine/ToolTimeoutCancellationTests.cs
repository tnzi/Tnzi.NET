using Tnzi.AI.Tools.Models;

namespace Tnzi.AI.Tests.Engine;

/// <summary>
/// 工具超时必须<b>取消</b>工具，而有自己上限的等待（审批、子 Agent）不占工具超时。
/// </summary>
/// <remarks>
/// 此前执行器只在等待上加超时（<c>WaitAsync</c>），交给工具的令牌从不取消：模型被告知「超时」并继续下一轮，
/// 原调用却还在后台跑。审批晚于工具超时到达时那个（可能是破坏性的）工具照样执行；轮询类工具与下一轮的工具、
/// 历史落库同时用同一个作用域的 DbContext。反过来，固定的 120 秒把审批窗口与 <c>SubAgentTimeout</c> 一起截断。
/// </remarks>
public class ToolTimeoutCancellationTests
{
    /// <summary>等条件成立的上限：条件一成立立即返回，按满载机器定。</summary>
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task TimedOutTool_HasItsTokenCancelled_AndTheModelIsToldItTimedOut()
    {
        var toolToken = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tool = AIFunctionFactory.Create(async (CancellationToken ct) =>
        {
            toolToken.TrySetResult(ct);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return "unreachable";
        }, "slow_tool");

        var (client, toolResults) = ToolThenDoneClient("slow_tool");
        var executor = new AgentExecutor(client, new AgentExecutorOptions { Name = "agent", Tools = [tool], ToolTimeoutSeconds = 1 });

        await executor.ExecuteAsync([new ChatMessage(ChatRole.User, "go")]).WaitAsync(Bound);

        (await toolToken.Task.WaitAsync(Bound)).IsCancellationRequested.ShouldBeTrue("a timed-out tool must be cancelled, not abandoned");
        toolResults.ShouldHaveSingleItem().ShouldContain("timed out");
    }

    [Fact]
    public async Task TimedOutTool_IgnoringItsToken_IsNotWaitedFor()
    {
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tool = AIFunctionFactory.Create(() => release.Task, "stubborn_tool");

        var (client, toolResults) = ToolThenDoneClient("stubborn_tool");
        var executor = new AgentExecutor(client, new AgentExecutorOptions { Name = "agent", Tools = [tool], ToolTimeoutSeconds = 1 });

        try
        {
            await executor.ExecuteAsync([new ChatMessage(ChatRole.User, "go")]).WaitAsync(Bound);
        }
        finally
        {
            release.TrySetResult("late");
        }

        toolResults.ShouldHaveSingleItem().ShouldContain("timed out");
    }

    [Fact]
    public async Task TimedOutGracefulTool_IsCancelledAfterItsGracePeriod()
    {
        var toolToken = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tool = AIFunctionFactory.Create(async (CancellationToken ct) =>
        {
            toolToken.TrySetResult(ct);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return "unreachable";
        }, "graceful_tool");

        var (client, toolResults) = ToolThenDoneClient("graceful_tool");
        var executor = new AgentExecutor(client, new AgentExecutorOptions
        {
            Name = "agent",
            Tools = [tool],
            ToolTimeoutSeconds = 1,
            ToolDefinitions = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["graceful_tool"] = new() { Name = "graceful_tool", InterruptBehavior = ToolInterruptBehavior.GracefulShutdown }
            }
        });

        await executor.ExecuteAsync([new ChatMessage(ChatRole.User, "go")]).WaitAsync(Bound);

        (await toolToken.Task.WaitAsync(Bound)).IsCancellationRequested.ShouldBeTrue("the grace period must end in cancellation");
        toolResults.ShouldHaveSingleItem().ShouldContain("timed out");
    }

    /// <summary>
    /// 审批窗口（ToolApproval:TimeoutSeconds）比工具超时长时，晚于工具超时到达的批准必须让工具真的执行，
    /// 并把结果交给模型，而不是模型先听到「超时」、工具事后才在后台执行。
    /// </summary>
    [Fact]
    public async Task ApprovalWaitLongerThanTheToolTimeout_DoesNotCountAgainstIt()
    {
        var executed = 0;
        var inner = AIFunctionFactory.Create(() =>
        {
            Interlocked.Increment(ref executed);
            return "executed";
        }, "guarded_tool");

        var approvalHandler = new Mock<IToolApprovalHandler>();
        approvalHandler.Setup(x => x.RequestApprovalAsync(It.IsAny<ToolApprovalRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (ToolApprovalRequest _, CancellationToken ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(2.5), ct);
                return new ToolApprovalResult { Approved = true, Status = ToolApprovalStatus.Approved };
            });

        var wrapped = ApprovalToolWrapper.Wrap(
            [inner],
            approvalHandler.Object,
            new ToolApprovalOptions { Enabled = true, Mode = ToolApprovalMode.AlwaysRequire, TimeoutSeconds = 30 });

        var (client, toolResults) = ToolThenDoneClient("guarded_tool");
        var executor = new AgentExecutor(client, new AgentExecutorOptions { Name = "agent", Tools = [.. wrapped], ToolTimeoutSeconds = 1 });

        await executor.ExecuteAsync([new ChatMessage(ChatRole.User, "go")]).WaitAsync(Bound);

        toolResults.ShouldHaveSingleItem().ShouldBe("executed");
        executed.ShouldBe(1);
    }

    /// <summary>
    /// 子 Agent 自带上限（SubAgentTimeout）：跑得比父 Agent 的工具超时久、但在 SubAgentTimeout 之内，结果要交回父 Agent。
    /// </summary>
    [Fact]
    public async Task ChildAgentRunningLongerThanTheToolTimeout_StillReturnsItsAnswer()
    {
        var childId = Guid.NewGuid();
        var strategy = new AgentAsToolsExecutionStrategy(new AgentAsToolsConfiguration
        {
            Agents = new Dictionary<string, Guid> { ["Researcher"] = childId },
            SubAgentTimeout = TimeSpan.FromSeconds(30)
        });

        var childClient = new Mock<IChatClient>();
        childClient.Setup(x => x.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(async (IEnumerable<ChatMessage> _, ChatOptions? _, CancellationToken ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(2.5), ct);
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, "child findings"));
            });

        var (parentClient, toolResults) = ToolThenDoneClient("call_researcher", new Dictionary<string, object?> { ["task"] = "dig" });
        var parent = new AgentExecutor(parentClient, new AgentExecutorOptions { Name = "Lead", ToolTimeoutSeconds = 1 });

        await strategy.ExecuteAsync(parent, [new ChatMessage(ChatRole.User, "go")], ChildContext(childId, "Researcher", childClient.Object), CancellationToken.None)
            .WaitAsync(Bound);

        toolResults.ShouldHaveSingleItem().ShouldBe("child findings");
    }

    [Fact]
    public async Task Suspend_StopsTheBudgetUntilResumed()
    {
        using var cts = new CancellationTokenSource();
        using (ToolCallTimeout.Begin(cts, TimeSpan.FromSeconds(2)))
        {
            using (ToolCallTimeout.Suspend())
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
                cts.IsCancellationRequested.ShouldBeFalse("a suspended budget must not run out");
            }

            (await WaitUntilAsync(() => cts.IsCancellationRequested)).ShouldBeTrue("the budget must resume after the suspension ends");
        }
    }

    [Fact]
    public void Suspend_OutsideAToolCall_IsANoOp()
    {
        using var scope = ToolCallTimeout.Suspend();
        scope.ShouldNotBeNull();
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Bound;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(10);
        }

        return true;
    }

    /// <summary>第一轮调用指定工具，第二轮收尾；记录模型在第二轮收到的工具结果。</summary>
    private static (IChatClient Client, List<string> ToolResults) ToolThenDoneClient(string toolName, IDictionary<string, object?>? arguments = null)
    {
        var toolResults = new List<string>();
        var calls = 0;
        var client = new Mock<IChatClient>();
        client.Setup(x => x.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<ChatMessage> messages, ChatOptions? _, CancellationToken _) =>
            {
                if (++calls == 1)
                {
                    return new ChatResponse([new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call_1", toolName, arguments)])]);
                }

                toolResults.AddRange(messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Select(r => r.Result?.ToString() ?? string.Empty));
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, "done"));
            });

        return (client.Object, toolResults);
    }

    private static ExecutionStrategyContext ChildContext(Guid childId, string childName, IChatClient childClient)
    {
        var repository = new Mock<IRepository<Agent, Guid>>();
        repository.Setup(x => x.GetAsync(childId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent { Id = childId, Name = childName, Provider = "test", IsEnabled = true });

        var agentFactory = new Mock<IAgentFactory>();
        agentFactory.Setup(x => x.CreateAgentAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), childName,
                It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(), It.IsAny<int?>(),
                It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(),
                childId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AgentExecutor(childClient, new AgentExecutorOptions { Name = childName }));

        return new ExecutionStrategyContext
        {
            AgentFactory = agentFactory.Object,
            AgentRepository = repository.Object,
            ServiceProvider = TestHelpers.ServiceProviderWithGrants(),
            ExecutionContextAccessor = new AgentExecutionContextAccessor(),
            Logger = Mock.Of<ILogger>()
        };
    }
}
