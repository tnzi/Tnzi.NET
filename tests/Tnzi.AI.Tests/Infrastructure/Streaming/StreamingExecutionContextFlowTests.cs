namespace Tnzi.AI.Tests.Infrastructure.Streaming;

/// <summary>
/// 流式运行里，工具与后续 LLM 调用必须看得到本次运行的执行上下文（当前请求、运行属性包、
/// 中间件设置的 AsyncLocal）。
/// </summary>
/// <remarks>
/// async iterator 的每次 MoveNextAsync 都在<b>调用方当时</b>的 ExecutionContext 上恢复，迭代器体内
/// 设置的 AsyncLocal 只活到第一个 yield。流式执行器总是先 yield 一个 chunk（文本 / 工具调用提示）
/// 再执行工具，所以工具一定跑在消费方（控制器 / ChatService）的上下文里：没有当前请求，属性包是空的。
/// 后果不只是功能失灵：绑定到用户的 Deny 规则因为取不到 UserId 而不匹配，同一个工具调用在非流式下
/// 被拒、在流式（界面默认路径）下放行。
/// 这里的消费方刻意用普通 <c>await foreach</c> 驱动，不预先实体化属性包，避免夹具替被测代码补上上下文。
/// </remarks>
public class StreamingExecutionContextFlowTests
{
    private const string ProbePropertyKey = "probe.middleware";
    private const string ToolPropertyKey = "probe.tool";

    private static readonly AsyncLocal<string?> MiddlewareAmbient = new();

    [Fact]
    public async Task RunStreamingAsync_ToolRunningAfterFirstYield_SeesTheRunsRequestAndPropertyBag()
    {
        var userId = Guid.NewGuid();
        var harness = new Harness();

        await foreach (var _ in harness.Runtime.RunStreamingAsync(harness.CreateRequest(userId)))
        {
        }

        harness.ToolInvocations.ShouldBe(1);
        harness.ToolSawUserId.ShouldBe(userId);
        harness.ToolSawMiddlewareProperty.ShouldBe("set-by-middleware");
        harness.ToolSawAmbient.ShouldBe("ambient");
        harness.ToolSawToolContext.ShouldBeTrue();
    }

    [Fact]
    public async Task RunStreamingAsync_SecondLlmCallOfTheToolLoop_SeesAmbientStateSetByMiddleware()
    {
        // ThinkingMiddleware 经 AsyncLocal 把推理参数交给 provider 管线；工具循环里的第二次及以后的 LLM 调用
        // 发生在 yield 之后，看不到它就等于后续请求不带推理参数
        var harness = new Harness();

        await foreach (var _ in harness.Runtime.RunStreamingAsync(harness.CreateRequest(Guid.NewGuid())))
        {
        }

        harness.LlmCallAmbient.ShouldBe(["ambient", "ambient"]);
    }

    [Fact]
    public async Task RunStreamingAsync_MiddlewareAfterBlock_SeesWhatTheToolWroteIntoThePropertyBag()
    {
        // ClarificationMiddleware / TodoMiddleware 在流尾读取工具写进属性包的内容
        var harness = new Harness();

        await foreach (var _ in harness.Runtime.RunStreamingAsync(harness.CreateRequest(Guid.NewGuid())))
        {
        }

        harness.AfterBlockSawToolProperty.ShouldBe(true);
    }

    [Fact]
    public async Task RunStreamingAsync_UserBoundDenyRule_RejectsTheToolCall()
    {
        var userId = Guid.NewGuid();
        var harness = new Harness(denyToolForUser: userId);

        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in harness.Runtime.RunStreamingAsync(harness.CreateRequest(userId)))
        {
            chunks.Add(chunk);
        }

        harness.ToolInvocations.ShouldBe(0);
        chunks.Where(c => c.ToolCalls != null).SelectMany(c => c.ToolCalls!).ShouldContain(d => d.Name == "probe_tool");
    }

    [Fact]
    public async Task RunAsync_UserBoundDenyRule_RejectsTheToolCall()
    {
        // 对照组：非流式路径本来就拒绝，流式必须给出同一个答案
        var userId = Guid.NewGuid();
        var harness = new Harness(denyToolForUser: userId);

        await harness.Runtime.RunAsync(harness.CreateRequest(userId));

        harness.ToolInvocations.ShouldBe(0);
    }

    [Fact]
    public async Task RunStreamingAsync_TrackedRun_ExposesTheRunIdToTools()
    {
        // spawn_agent 靠 CurrentRunId 给子运行挂 ParentRunId；审批与产物也按它归档
        var harness = new Harness();
        var request = harness.CreateRequest(Guid.NewGuid(), trackRun: true);

        await foreach (var _ in harness.Runtime.RunStreamingAsync(request))
        {
        }

        harness.ToolSawRunId.ShouldBe(harness.Run.Id);
    }

    [Fact]
    public async Task RunAsync_TrackedRun_ExposesTheRunIdToTools()
    {
        // 运行 id 在解析阶段（一个普通 async 方法）里写进属性包：属性包若在那里才惰性创建，
        // 这次赋值传不回运行本身，工具读到的是另一个空包
        var harness = new Harness();
        var request = harness.CreateRequest(Guid.NewGuid(), trackRun: true);

        await harness.Runtime.RunAsync(request);

        harness.ToolSawRunId.ShouldBe(harness.Run.Id);
    }

    private sealed class Harness
    {
        private readonly AgentExecutionContextAccessor _accessor = new();
        private readonly Guid _agentId = Guid.NewGuid();
        private int _llmCalls;

        public Harness(Guid? denyToolForUser = null)
        {
            Run = new AgentRun { Id = Guid.NewGuid(), AgentId = _agentId, Status = AgentRunStatus.Pending, Nodes = [] };

            IList<AITool> tools = [AIFunctionFactory.Create(ProbeTool, "probe_tool", "Probe tool")];
            if (denyToolForUser.HasValue)
            {
                var evaluator = new ToolPermissionEvaluator(
                [
                    new ToolPermissionRule
                    {
                        ToolPattern = "probe_tool",
                        Behavior = PermissionBehavior.Deny,
                        Scope = ToolPermissionScope.User,
                        UserId = denyToolForUser,
                        Reason = "Denied for this user"
                    }
                ]);
                tools = ApprovalToolWrapper.Wrap(tools, null, new ToolApprovalOptions(), evaluator, executionContextAccessor: _accessor);
            }

            var executor = new AgentExecutor(CreateChatClient(), new AgentExecutorOptions { Name = "probe", Tools = [.. tools] });

            var resolver = new Mock<IAgentResolver>();
            resolver.Setup(x => x.ResolveAgentAsync(_agentId, null, null, null, It.IsAny<CancellationToken>(), null))
                .ReturnsAsync(AgentResolution.Success(executor, "test", "gpt-5", _agentId));

            var runStore = new Mock<IRunStore>();
            runStore.Setup(x => x.GetAsync(Run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Run);
            runStore.Setup(x => x.UpdateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            var traceStore = new Mock<ITraceStore>();
            traceStore.Setup(x => x.AddAsync(It.IsAny<AgentRunTrace>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((AgentRunTrace trace, CancellationToken _) => trace);

            var sp = new ServiceCollection()
                .AddSingleton<IAiMiddleware>(new ProbeMiddleware(this, _accessor))
                .BuildServiceProvider();

            Runtime = new AgentRuntime(
                resolver.Object,
                Mock.Of<IAgentFactory>(),
                Mock.Of<IRepository<Agent, Guid>>(),
                new RunTracker(runStore.Object, traceStore.Object, Mock.Of<ILogger<RunTracker>>()),
                Mock.Of<IWorkflowDelegator>(),
                _accessor,
                sp,
                Mock.Of<IOptionsMonitor<AIOptions>>(),
                Mock.Of<IEventPublisher>(),
                Mock.Of<ILogger<AgentRuntime>>());
        }

        public AgentRuntime Runtime { get; }
        public AgentRun Run { get; }
        public int ToolInvocations { get; private set; }
        public Guid? ToolSawUserId { get; private set; }
        public object? ToolSawMiddlewareProperty { get; private set; }
        public string? ToolSawAmbient { get; private set; }
        public object? ToolSawRunId { get; private set; }
        public bool ToolSawToolContext { get; private set; }
        public bool? AfterBlockSawToolProperty { get; set; }
        public List<string?> LlmCallAmbient { get; } = [];

        public AgentRunRequest CreateRequest(Guid userId, bool trackRun = false) => new()
        {
            AgentId = _agentId,
            UserId = userId,
            UserMessage = "check the thing",
            EnableRunTracking = trackRun,
            ExistingRunId = trackRun ? Run.Id : null
        };

        private string ProbeTool()
        {
            ToolInvocations++;
            ToolSawUserId = _accessor.CurrentRequest?.UserId;
            ToolSawMiddlewareProperty = _accessor.Properties.GetValueOrDefault(ProbePropertyKey);
            ToolSawRunId = _accessor.Properties.GetValueOrDefault(ContextPropertyKeys.CurrentRunId);
            ToolSawAmbient = MiddlewareAmbient.Value;
            ToolSawToolContext = ToolContextAccessor.Current != null;
            _accessor.Properties[ToolPropertyKey] = true;
            return "ok";
        }

        private IChatClient CreateChatClient()
        {
            var toolCall = new FunctionCallContent("call_1", "probe_tool", new Dictionary<string, object?>());
            var client = new Mock<IChatClient>();
            client.Setup(x => x.GetStreamingResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
                .Returns(() => StreamCall(++_llmCalls == 1, toolCall));
            client.Setup(x => x.GetResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    LlmCallAmbient.Add(MiddlewareAmbient.Value);
                    return ++_llmCalls == 1
                        ? new ChatResponse([new ChatMessage(ChatRole.Assistant, [toolCall])])
                        : new ChatResponse(new ChatMessage(ChatRole.Assistant, "done"));
                });
            return client.Object;
        }

        private async IAsyncEnumerable<ChatResponseUpdate> StreamCall(bool firstCall, FunctionCallContent toolCall)
        {
            LlmCallAmbient.Add(MiddlewareAmbient.Value);
            await Task.Yield();
            if (firstCall)
            {
                // 先流一段文本：执行器会把它 yield 出去，工具随后才执行
                yield return new ChatResponseUpdate(ChatRole.Assistant, "Let me check.");
                await Task.Yield();
                yield return new ChatResponseUpdate(ChatRole.Assistant, [toolCall]);
                yield break;
            }

            yield return new ChatResponseUpdate(ChatRole.Assistant, "done") { FinishReason = ChatFinishReason.Stop };
        }
    }

    /// <summary>在 before 段设置属性包与 AsyncLocal，在 after 段读取工具写入的属性，与真实中间件同形。</summary>
    private sealed class ProbeMiddleware(Harness harness, IAgentExecutionContextAccessor accessor) : IAiMiddleware
    {
        public int Order => AiMiddlewareOrders.History;

        public async Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
        {
            accessor.Properties[ProbePropertyKey] = "set-by-middleware";
            MiddlewareAmbient.Value = "ambient";
            var result = await next(context, cancellationToken);
            harness.AfterBlockSawToolProperty = accessor.Properties.ContainsKey(ToolPropertyKey);
            return result;
        }

        public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            accessor.Properties[ProbePropertyKey] = "set-by-middleware";
            MiddlewareAmbient.Value = "ambient";
            await foreach (var chunk in next(context, cancellationToken))
            {
                yield return chunk;
            }
            harness.AfterBlockSawToolProperty = accessor.Properties.ContainsKey(ToolPropertyKey);
        }
    }
}
