using Tnzi.AI.Tools.Attributes;

namespace Tnzi.AI.Tests;

/// <summary>
/// 子 Agent 模板的 Instructions / MaxTurns / ExcludedToolGroups 必须真的落到 spawn 起的运行上。
/// </summary>
/// <remarks>
/// 此前 spawn 只用模板的 ToolGroups 与 DefaultModel：模板运行没有 AgentId，解析器的临时 Agent 路径建出一个
/// 不带系统指令的执行器，工具循环上限是执行器默认的 25 次，排除的工具组只是存着、列着、能编辑。
/// 管理员建一个「只审不改、最多 5 轮、不给沙箱」的 reviewer 类型，spawn 出来的运行三条一条都不遵守。
/// 这里用真实执行器跑运行时，看模型实际收到的系统提示、工具清单与调用次数。
/// </remarks>
public class SubAgentTemplateRunSettingsTests
{
    private const string TemplateInstructions = "Only review, never edit.";

    [Fact]
    public async Task RunAsync_TemplateRun_UsesTheTemplateInstructionsTurnLimitAndExclusions()
    {
        var harness = new Harness();

        await harness.Runtime.RunAsync(harness.TemplateRequest());

        harness.Client.Calls.Count.ShouldBe(2, "MaxTurns caps the tool loop");
        harness.Client.Calls.ShouldAllBe(c => c.Instructions != null && c.Instructions.Contains(TemplateInstructions));
        harness.Client.Calls.ShouldAllBe(c => c.ToolNames.Contains("probe_lookup") && !c.ToolNames.Contains("probe_edit"));
    }

    [Fact]
    public async Task RunStreamingAsync_TemplateRun_UsesTheTemplateInstructionsTurnLimitAndExclusions()
    {
        var harness = new Harness();

        await foreach (var _ in harness.Runtime.RunStreamingAsync(harness.TemplateRequest()))
        {
        }

        harness.Client.Calls.Count.ShouldBe(2, "MaxTurns caps the tool loop");
        harness.Client.Calls.ShouldAllBe(c => c.Instructions != null && c.Instructions.Contains(TemplateInstructions));
        harness.Client.Calls.ShouldAllBe(c => c.ToolNames.Contains("probe_lookup") && !c.ToolNames.Contains("probe_edit"));
    }

    [Fact]
    public async Task RunAsync_RequestWithoutTemplateSettings_KeepsTheExecutorAsResolved()
    {
        var harness = new Harness();

        await harness.Runtime.RunAsync(new AgentRunRequest { UserMessage = "review the diff", IsBackground = true, SubAgentName = "plain" });

        harness.Client.Calls.Count.ShouldBe(Harness.ResolvedMaxToolIterations);
        harness.Client.Calls.ShouldAllBe(c => c.Instructions == null);
        harness.Client.Calls.ShouldAllBe(c => c.ToolNames.Contains("probe_edit"));
    }

    private sealed class Harness
    {
        /// <summary>解析出来的执行器自己的上限：模板把它压到 2，没有模板时应原样保留。</summary>
        public const int ResolvedMaxToolIterations = 4;

        public Harness()
        {
            var definitions = new ToolScanner(NullLogger<ToolScanner>.Instance)
                .ScanAssembly(typeof(SubAgentTemplateRunSettingsTests).Assembly)
                .Where(t => t.ProviderType == typeof(KeptTools) || t.ProviderType == typeof(ExcludedTools))
                .ToList();
            var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
            foreach (var definition in definitions)
            {
                registry.Register(definition);
            }

            var toolServices = new ServiceCollection()
                .AddSingleton<KeptTools>()
                .AddSingleton<ExcludedTools>()
                .BuildServiceProvider();

            var executor = new AgentExecutor(Client, new AgentExecutorOptions
            {
                Name = "ad-hoc",
                Tools = ToolAdapter.ConvertToAITools(definitions, toolServices),
                ToolDefinitions = definitions.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase),
                MaxToolIterations = ResolvedMaxToolIterations
            });

            var resolver = new Mock<IAgentResolver>();
            resolver.Setup(x => x.ResolveAgentAsync(It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<List<string>?>(), It.IsAny<CancellationToken>(), It.IsAny<List<string>?>()))
                .ReturnsAsync(AgentResolution.Success(executor, "test", "gpt-5", null));
            resolver.Setup(x => x.BuildChatMessageAsync(It.IsAny<string?>(), It.IsAny<List<ContentPartDto>?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string? message, List<ContentPartDto>? _, CancellationToken _) => new ChatMessage(ChatRole.User, message));

            Runtime = new AgentRuntime(
                resolver.Object,
                Mock.Of<IAgentFactory>(),
                Mock.Of<IRepository<Agent, Guid>>(),
                new RunTracker(Mock.Of<IRunStore>(), Mock.Of<ITraceStore>(), Mock.Of<ILogger<RunTracker>>()),
                Mock.Of<IWorkflowDelegator>(),
                new AgentExecutionContextAccessor(),
                new ServiceCollection().BuildServiceProvider(),
                new StaticOptionsMonitor<AIOptions>(new AIOptions()),
                Mock.Of<IEventPublisher>(),
                Mock.Of<ILogger<AgentRuntime>>(),
                toolRegistry: registry);
        }

        public LoopingChatClient Client { get; } = new();

        public AgentRuntime Runtime { get; }

        public AgentRunRequest TemplateRequest() => new()
        {
            UserMessage = "review the diff",
            IsBackground = true,
            SubAgentName = "reviewer",
            SubAgentInstructions = TemplateInstructions,
            MaxToolIterations = 2,
            ExcludedToolGroups = ["probe-excluded"]
        };
    }

    /// <summary>每一轮都要一次 probe_lookup：工具循环只会在上限处停下，调用次数就是实际生效的上限。</summary>
    private sealed class LoopingChatClient : IChatClient
    {
        private int _sequence;

        public List<(string? Instructions, List<string> ToolNames)> Calls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Record(options);
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, [NextCall()])]));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Record(options);
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, [NextCall()]);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }

        private void Record(ChatOptions? options)
        {
            lock (Calls)
            {
                Calls.Add((options?.Instructions, options?.Tools?.Select(t => t.Name).ToList() ?? []));
            }
        }

        private FunctionCallContent NextCall()
            => new($"call_{Interlocked.Increment(ref _sequence)}", "probe_lookup");
    }

    [AIToolGroup("probe-kept", "Probe Kept", "Tools the template keeps")]
    private sealed class KeptTools : IAIToolProvider
    {
        [AIFunction("probe_lookup", "Looks something up", IsReadOnly = true)]
        public Task<string> LookupAsync() => Task.FromResult("found");
    }

    [AIToolGroup("probe-excluded", "Probe Excluded", "Tools the template excludes")]
    private sealed class ExcludedTools : IAIToolProvider
    {
        [AIFunction("probe_edit", "Edits something")]
        public Task<string> EditAsync() => Task.FromResult("edited");
    }
}
