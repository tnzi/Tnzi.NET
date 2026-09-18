using Tnzi.Exceptions;

namespace Tnzi.AI.Tests;

/// <summary>
/// 无 AgentId 的请求自选工具组 / 工具名（HTTP 聊天体的 <c>toolGroups</c>）必须过服务端允许列表。
/// </summary>
/// <remarks>
/// 此前 <c>DefaultChatController.Chat</c> 只要求登录，请求体的 <c>toolGroups</c> 原样进解析器，
/// 任何登录用户自填 <c>["sandbox"]</c> 就能拿到宿主上的 bash。允许列表默认为空 = 全部拒绝（失败关闭）；
/// 进程内调用方（<c>ITnziAiClient</c>、子 Agent 模板）经 <see cref="AgentRunRequest.TrustedToolSelection"/> 放行，
/// 该标记不映射任何 HTTP 请求体。
/// </remarks>
public class AdHocToolSelectionAllowListTests
{
    [Fact]
    public async Task RunAsync_UntrustedGroupNotInAllowList_Throws403AndNeverResolvesAnAgent()
    {
        var harness = new RuntimeHarness(allowedGroups: []);

        var ex = await Should.ThrowAsync<BusinessException>(() => harness.Runtime.RunAsync(new AgentRunRequest
        {
            UserMessage = "run id",
            ToolGroups = ["sandbox"]
        }));

        ex.HttpStatusCode.ShouldBe(403);
        ex.Code.ShouldBe(ErrorCodes.ToolGroupNotAllowed);
        ex.Message.ShouldContain("sandbox");
        harness.ResolveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task RunAsync_UntrustedToolNameNotInAllowList_Throws403()
    {
        var harness = new RuntimeHarness(allowedGroups: ["datetime"], allowedTools: []);

        var ex = await Should.ThrowAsync<BusinessException>(() => harness.Runtime.RunAsync(new AgentRunRequest
        {
            UserMessage = "run id",
            ToolNames = ["bash"]
        }));

        ex.HttpStatusCode.ShouldBe(403);
        ex.Code.ShouldBe(ErrorCodes.ToolNameNotAllowed);
        harness.ResolveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task RunAsync_MixedAllowedAndDisallowedGroups_RejectsInsteadOfSilentlyDropping()
    {
        // 静默丢掉不允许的组 = 调用方以为拿到了 sandbox，模型只是回答「我没有那个工具」
        var harness = new RuntimeHarness(allowedGroups: ["datetime"]);

        var ex = await Should.ThrowAsync<BusinessException>(() => harness.Runtime.RunAsync(new AgentRunRequest
        {
            UserMessage = "what time is it",
            ToolGroups = ["datetime", "sandbox"]
        }));

        ex.HttpStatusCode.ShouldBe(403);
        harness.ResolveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task RunAsync_AllowListedGroup_ReachesTheResolverWithThatGroup()
    {
        var harness = new RuntimeHarness(allowedGroups: ["DateTime"]);

        // 解析器被刻意做成失败：这里只验证请求穿过了允许列表，不跑管线
        await Should.ThrowAsync<BusinessException>(() => harness.Runtime.RunAsync(new AgentRunRequest
        {
            UserMessage = "what time is it",
            ToolGroups = ["datetime"]
        }));

        harness.ResolveCalls.ShouldBe(1);
        harness.LastResolvedGroups.ShouldBe(["datetime"]);
    }

    [Fact]
    public async Task RunAsync_TrustedToolSelection_BypassesTheAllowList()
    {
        // 进程内调用方（ITnziAiClient / 子 Agent 模板）自己决定工具组，不受面向 HTTP 的允许列表约束
        var harness = new RuntimeHarness(allowedGroups: []);

        await Should.ThrowAsync<BusinessException>(() => harness.Runtime.RunAsync(new AgentRunRequest
        {
            UserMessage = "run id",
            ToolGroups = ["sandbox"],
            TrustedToolSelection = true
        }));

        harness.ResolveCalls.ShouldBe(1);
        harness.LastResolvedGroups.ShouldBe(["sandbox"]);
    }

    [Fact]
    public async Task RunAsync_WithAgentId_RequestGroupsAreIgnoredSoNoAllowListCheck()
    {
        // 有 AgentId 时解析器走实体授权（grant 投影），请求体的组根本不参与，不该因它 403
        var harness = new RuntimeHarness(allowedGroups: []);

        await Should.ThrowAsync<BusinessException>(() => harness.Runtime.RunAsync(new AgentRunRequest
        {
            AgentId = Guid.NewGuid(),
            UserMessage = "hi",
            ToolGroups = ["sandbox"]
        }));

        harness.ResolveCalls.ShouldBe(1);
    }

    [Fact]
    public async Task RunStreamingAsync_UntrustedGroupNotInAllowList_Throws403()
    {
        var harness = new RuntimeHarness(allowedGroups: []);

        var ex = await Should.ThrowAsync<BusinessException>(async () =>
        {
            await foreach (var _ in harness.Runtime.RunStreamingAsync(new AgentRunRequest
            {
                UserMessage = "run id",
                ToolGroups = ["sandbox"]
            }))
            {
            }
        });

        ex.HttpStatusCode.ShouldBe(403);
        harness.ResolveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task TnziAiClient_MarksItsRequestsTrusted()
    {
        // 进程内客户端的工具组来自应用代码，不是 HTTP 请求体
        AgentRunRequest? captured = null;
        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(r => r.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AgentRunRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new AgentRunResult { Response = "ok", FinishReason = FinishReasons.Stop });

        var client = new TnziAiClient(runtime.Object, null);
        await client.ChatAsync("hi", Guid.NewGuid(), new AiClientOptions { ToolGroups = ["sandbox"] });

        captured.ShouldNotBeNull();
        captured!.TrustedToolSelection.ShouldBeTrue();
    }

    private sealed class RuntimeHarness
    {
        public RuntimeHarness(List<string>? allowedGroups = null, List<string>? allowedTools = null)
        {
            var accessor = new AgentExecutionContextAccessor();
            _ = accessor.Properties;

            var resolver = new Mock<IAgentResolver>();
            resolver
                .Setup(x => x.ResolveAgentAsync(It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<List<string>?>(), It.IsAny<CancellationToken>(), It.IsAny<List<string>?>()))
                .ReturnsAsync((Guid? agentId, string? provider, string? model, List<string>? groups, CancellationToken _, List<string>? names) =>
                {
                    ResolveCalls++;
                    LastResolvedGroups = groups;
                    return AgentResolution.Failure(provider ?? "OpenAI", model, agentId, ErrorCodes.AgentNotFound);
                });

            var runStore = new Mock<IRunStore>();
            var traceStore = new Mock<ITraceStore>();
            var runTracker = new RunTracker(runStore.Object, traceStore.Object, Mock.Of<ILogger<RunTracker>>());

            var options = new AIOptions
            {
                AdHocTools = new AdHocToolsOptions
                {
                    AllowedGroups = allowedGroups ?? [],
                    AllowedTools = allowedTools ?? []
                }
            };

            Runtime = new AgentRuntime(
                resolver.Object,
                Mock.Of<IAgentFactory>(),
                Mock.Of<IRepository<Agent, Guid>>(),
                runTracker,
                new WorkflowDelegator(Mock.Of<IWorkflowService>(), runStore.Object, runTracker),
                accessor,
                new ServiceCollection().BuildServiceProvider(),
                new StaticOptionsMonitor<AIOptions>(options),
                new EventPublisher(null, Mock.Of<IServiceScopeFactory>(), Mock.Of<ILogger<EventPublisher>>()),
                Mock.Of<ILogger<AgentRuntime>>());
        }

        public AgentRuntime Runtime { get; }
        public int ResolveCalls { get; private set; }
        public List<string>? LastResolvedGroups { get; private set; }
    }
}
