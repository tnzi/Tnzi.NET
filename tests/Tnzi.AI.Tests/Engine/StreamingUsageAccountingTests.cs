namespace Tnzi.AI.Tests.Engine;

/// <summary>
/// 流式路径必须把每一次 LLM 调用的用量都送到流里，与非流式路径计同一个数。
/// </summary>
/// <remarks>
/// <see cref="AgentStreamChunk.Usage"/> 是一次 LLM 调用的用量增量，消费方（配额结算、用量日志、
/// 消息持久化、SSE 终止事件）逐个相加。此前执行器只在最后一轮把<b>最后一次</b>调用的用量放进终止 chunk：
/// 三次工具调用加一次作答计四次费，配额与成本只记第四次；跑满 MaxToolIterations 一个都不记，
/// 预留的配额被全额退回。多 Agent 策略里，Router / Handoff 转接前那个 Agent 的调用被整段丢弃，
/// AgentAsTools 子 Agent 的用量排在终止 chunk 之后，消费方已经发完终止事件才看到它。
/// 这里的期望一律是「把流里每个 chunk 的 Usage 相加」。
/// </remarks>
public class StreamingUsageAccountingTests
{
    [Fact]
    public async Task Executor_ToolLoop_EveryLlmCallReachesTheStream()
    {
        var calls = 0;
        var executor = CreateExecutor(() => ++calls switch
        {
            <= 3 => Stream(ToolCall($"call_{calls}"), Usage(calls)),
            _ => Stream(Text("answer"), Usage(calls), Stop())
        });

        var chunks = await CollectAsync(executor.ExecuteStreamingAsync([new ChatMessage(ChatRole.User, "go")]));

        calls.ShouldBe(4);
        Sum(chunks).ShouldBe((1000, 100));
    }

    [Fact]
    public async Task Executor_MaxToolIterations_StillReportsEveryLlmCall()
    {
        // 跑满上限的这条路以前一个用量 chunk 都不发：配额按 0 结算，预留被全额退回
        var calls = 0;
        var executor = CreateExecutor(() => Stream(ToolCall($"call_{++calls}"), Usage(calls)), maxToolIterations: 2);

        var chunks = await CollectAsync(executor.ExecuteStreamingAsync([new ChatMessage(ChatRole.User, "go")]));

        calls.ShouldBe(2);
        chunks.ShouldContain(c => c.FinishReason == FinishReasons.MaxToolIterations);
        Sum(chunks).ShouldBe((300, 30));
    }

    [Fact]
    public async Task Executor_StreamingAndNonStreaming_MeterTheSameConversation()
    {
        var streamingCalls = 0;
        var nonStreamingCalls = 0;
        var client = new Mock<IChatClient>();
        client.Setup(x => x.GetStreamingResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() => ++streamingCalls <= 2
                ? Stream(ToolCall($"call_{streamingCalls}"), Usage(streamingCalls))
                : Stream(Text("answer"), Usage(streamingCalls), Stop()));
        client.Setup(x => x.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var call = ++nonStreamingCalls;
                var message = call <= 2
                    ? new ChatMessage(ChatRole.Assistant, [new FunctionCallContent($"call_{call}", "lookup", new Dictionary<string, object?>())])
                    : new ChatMessage(ChatRole.Assistant, "answer");
                return new ChatResponse(message) { Usage = UsageDetailsFor(call) };
            });
        var executor = new AgentExecutor(client.Object, new AgentExecutorOptions { Name = "agent", Tools = [LookupTool()] });

        var response = await executor.ExecuteAsync([new ChatMessage(ChatRole.User, "go")]);
        var chunks = await CollectAsync(executor.ExecuteStreamingAsync([new ChatMessage(ChatRole.User, "go")]));

        Sum(chunks).ShouldBe((response.Usage!.InputTokens, response.Usage.OutputTokens));
        Sum(chunks).ShouldBe((600, 60));
    }

    [Fact]
    public async Task AgentAsTools_Streaming_ReportsChildUsageBeforeTheTerminalChunk()
    {
        // ChatService 在第一个带 FinishReason 的 chunk 上就发出 SSE 终止事件：子 Agent 的用量必须先到
        var childId = Guid.NewGuid();
        var parentCalls = 0;
        var parent = CreateExecutor(() => ++parentCalls == 1
            ? Stream(new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("c1", "call_helper", new Dictionary<string, object?> { ["task"] = "look it up" })]), Usage(1))
            : Stream(Text("done"), Usage(2), Stop()));
        var context = CreateStrategyContext(childId, "helper", CreateNonStreamingAgent("helper", input: 1000, output: 50));
        var strategy = new AgentAsToolsExecutionStrategy(new AgentAsToolsConfiguration
        {
            Agents = new Dictionary<string, Guid> { ["helper"] = childId }
        });

        var chunks = await CollectAsync(strategy.ExecuteStreamingAsync(parent, [new ChatMessage(ChatRole.User, "go")], context, CancellationToken.None));

        Sum(chunks).ShouldBe((1300, 80));
        var beforeTerminal = chunks.TakeWhile(c => c.FinishReason == null).ToList();
        var terminal = chunks.First(c => c.FinishReason != null);
        Sum([.. beforeTerminal, terminal]).ShouldBe((1300, 80));
    }

    [Fact]
    public async Task Router_Streaming_IncludesTheRoutersOwnLlmCall()
    {
        var targetId = Guid.NewGuid();
        var router = CreateExecutor(() => Stream(ToolCall("r1", "route_to_agent", "specialist"), Usage(1)));
        var context = CreateStrategyContext(targetId, "Specialist",
            CreateExecutor(() => Stream(Text("answer"), UsageOf(7, 3), Stop())));
        var strategy = new RouterExecutionStrategy(new RouterConfiguration
        {
            Targets = new Dictionary<string, Guid> { ["specialist"] = targetId },
            AllowDirectResponse = false
        });

        var chunks = await CollectAsync(strategy.ExecuteStreamingAsync(router, [new ChatMessage(ChatRole.User, "go")], context, CancellationToken.None));

        chunks.ShouldContain(c => c.Text == "answer");
        Sum(chunks).ShouldBe((107, 13));
    }

    [Fact]
    public async Task Handoff_Streaming_IncludesTheTransferringAgentsCallAndDoesNotStartAnotherOne()
    {
        // 转接一旦发生，转出方后续的 LLM 调用只会被丢弃：不该再发起（白花钱且量不到）
        var targetId = Guid.NewGuid();
        var triageCalls = 0;
        var triage = CreateExecutor(() => ++triageCalls == 1
            ? Stream(ToolCall("h1", "handoff_to_agent", "specialist"), Usage(1))
            : Stream(Text("discarded"), Usage(2), Stop()));
        var context = CreateStrategyContext(targetId, "Specialist",
            CreateExecutor(() => Stream(Text("answer"), UsageOf(7, 3), Stop())));
        var strategy = new HandoffExecutionStrategy(new HandoffConfiguration
        {
            Targets = new Dictionary<string, Guid> { ["specialist"] = targetId },
            MaxHandoffs = 3
        });

        var chunks = await CollectAsync(strategy.ExecuteStreamingAsync(triage, [new ChatMessage(ChatRole.User, "go")], context, CancellationToken.None));

        triageCalls.ShouldBe(1);
        chunks.ShouldContain(c => c.Text == "answer");
        chunks.ShouldNotContain(c => c.Text == "discarded");
        Sum(chunks).ShouldBe((107, 13));
    }

    [Fact]
    public async Task OutputGuardrail_Rejection_CarriesTheUsageOfTheDiscardedChunks()
    {
        var options = new AIOptions { Guardrails = new GuardrailsOptions { Enabled = true, StreamingBufferSize = 5, StreamingOverlapSize = 0 } };
        var guardrail = new Mock<IOutputGuardrail>();
        guardrail.Setup(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GuardrailResult.Rejected("test", "blocked"));
        var middleware = new OutputGuardrailMiddleware(
            new GuardrailRunner([], [guardrail.Object], new StaticOptionsMonitor<AIOptions>(options), Mock.Of<ILogger<GuardrailRunner>>()),
            [guardrail.Object],
            new StaticOptionsMonitor<AIOptions>(options),
            Mock.Of<ILogger<OutputGuardrailMiddleware>>());

        var upstream = new[]
        {
            new AgentStreamChunk { Usage = new TokenUsageDto { InputTokens = 100, OutputTokens = 10 } },
            new AgentStreamChunk { Text = "harmful text" }
        };

        var chunks = await CollectAsync(middleware.InvokeStreamingAsync(CreateMiddlewareContext(), (_, _) => upstream.ToAsyncEnumerable()));

        chunks.ShouldContain(c => c.FinishReason == FinishReasons.GuardrailRejected);
        chunks.ShouldNotContain(c => c.Text == "harmful text");
        Sum(chunks).ShouldBe((100, 10));
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static (int Input, int Output) Sum(IEnumerable<AgentStreamChunk> chunks)
        => (chunks.Sum(c => c.Usage?.InputTokens ?? 0), chunks.Sum(c => c.Usage?.OutputTokens ?? 0));

    private static async Task<List<AgentStreamChunk>> CollectAsync(IAsyncEnumerable<AgentStreamChunk> stream)
    {
        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in stream)
        {
            chunks.Add(chunk);
        }
        return chunks;
    }

    private static AgentExecutor CreateExecutor(Func<IAsyncEnumerable<ChatResponseUpdate>> nextCall, int maxToolIterations = 25)
    {
        var client = new Mock<IChatClient>();
        client.Setup(x => x.GetStreamingResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() => nextCall());
        return new AgentExecutor(client.Object, new AgentExecutorOptions
        {
            Name = "agent",
            Tools = [LookupTool()],
            MaxToolIterations = maxToolIterations
        });
    }

    private static AgentExecutor CreateNonStreamingAgent(string name, int input, int output)
    {
        var client = new Mock<IChatClient>();
        client.Setup(x => x.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "child result"))
            {
                Usage = new UsageDetails { InputTokenCount = input, OutputTokenCount = output }
            });
        return new AgentExecutor(client.Object, new AgentExecutorOptions { Name = name });
    }

    private static ExecutionStrategyContext CreateStrategyContext(Guid targetId, string targetName, IAgentExecutor target)
    {
        var repository = new Mock<IRepository<Agent, Guid>>();
        repository.Setup(x => x.GetAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent { Id = targetId, Name = targetName, Provider = "test", IsEnabled = true });

        var factory = new Mock<IAgentFactory>();
        factory.Setup(x => x.CreateAgentAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), targetName,
                It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(), It.IsAny<int?>(),
                It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(),
                targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        return new ExecutionStrategyContext
        {
            AgentFactory = factory.Object,
            AgentRepository = repository.Object,
            ServiceProvider = TestHelpers.ServiceProviderWithGrants(),
            ExecutionContextAccessor = new AgentExecutionContextAccessor(),
            Logger = Mock.Of<ILogger>()
        };
    }

    private static AiMiddlewareContext CreateMiddlewareContext() => new()
    {
        Request = new AgentRunRequest { UserMessage = "Hello", UserId = Guid.NewGuid() },
        Agent = AgentResolution.Success(agent: null!, provider: "test", model: "test-model", agentId: null),
        ServiceProvider = new ServiceCollection().BuildServiceProvider()
    };

    private static AIFunction LookupTool() => AIFunctionFactory.Create(() => "found", "lookup", "Lookup");

    /// <summary>第 <paramref name="call"/> 次 LLM 调用的用量：输入 100×n，输出 10×n。</summary>
    private static ChatResponseUpdate Usage(int call) => UsageOf(100 * call, 10 * call);

    private static ChatResponseUpdate UsageOf(int input, int output)
        => new() { Contents = [new UsageContent(new UsageDetails { InputTokenCount = input, OutputTokenCount = output, TotalTokenCount = input + output })] };

    private static UsageDetails UsageDetailsFor(int call)
        => new() { InputTokenCount = 100 * call, OutputTokenCount = 10 * call, TotalTokenCount = 110 * call };

    private static ChatResponseUpdate ToolCall(string callId, string name = "lookup", string? target = null)
        => new(ChatRole.Assistant, [new FunctionCallContent(callId, name, target == null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?> { ["targetAgentName"] = target, ["reason"] = "better suited" })]);

    private static ChatResponseUpdate Text(string text) => new(ChatRole.Assistant, text);

    private static ChatResponseUpdate Stop() => new() { FinishReason = ChatFinishReason.Stop };

    private static async IAsyncEnumerable<ChatResponseUpdate> Stream(params ChatResponseUpdate[] updates)
    {
        foreach (var update in updates)
        {
            await Task.Yield();
            yield return update;
        }
    }
}
