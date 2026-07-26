using Tnzi.AI.Sandbox.Middleware;
using Tnzi.AI.Sandbox.Tools;
using Tnzi.AI.Tools.Models;

namespace Tnzi.AI.Tests.Sandbox;

/// <summary>
/// P0 回归锁定 - 沙箱工具环境参数绑定集成测试。
/// </summary>
/// <remarks>
/// 贯穿真实链路：ToolScanner 扫描 Sandbox 程序集 → ToolRegistry → ToolResolver
/// （内部 ToolAdapter/AIFunctionFactory）→ 真实 AgentExecutor 工具调用循环 →
/// SandboxTools 从 IAgentExecutionContextAccessor 解析 SandboxMiddleware 发布的沙箱环境。
/// 旧形态（方法签名携带 ISandbox sandbox, Guid threadId 且无 [AIFunction]/IAIToolProvider）下，
/// 扫描发现 0 个工具、schema 暴露不可反序列化的接口参数 - 本文件中的测试必然失败。
/// </remarks>
public class SandboxToolBindingIntegrationTests : IDisposable
{
    private readonly string _workDir;
    private readonly VirtualPathTranslator _translator;

    public SandboxToolBindingIntegrationTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"tnzi-sbx-int-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
        _translator = new VirtualPathTranslator(_workDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workDir))
            Directory.Delete(_workDir, recursive: true);
    }

    // -------------------------------------------------------------------------
    // 1. 扫描与 schema - 旧形态下扫描结果为空 / schema 含环境参数
    // -------------------------------------------------------------------------

    [Fact]
    public void ToolScanner_DiscoversAllFiveSandboxTools()
    {
        var definitions = ScanSandboxTools();

        definitions.Select(d => d.Name)
            .ShouldBe(["bash", "ls", "read_file", "write_file", "str_replace"], ignoreOrder: true);
        definitions.ShouldAllBe(d => d.GroupName == "sandbox");
    }

    [Fact]
    public async Task ToolSchemas_ExposeOnlyLlmParameters()
    {
        var tools = await ResolveSandboxToolsAsync(BuildServiceProvider());

        foreach (var tool in tools.OfType<AIFunction>())
        {
            tool.JsonSchema.TryGetProperty("properties", out var properties)
                .ShouldBeTrue($"tool '{tool.Name}' should expose a parameter schema");
            properties.TryGetProperty("sandbox", out _)
                .ShouldBeFalse($"tool '{tool.Name}' must not leak ISandbox into the LLM schema");
            properties.TryGetProperty("threadId", out _)
                .ShouldBeFalse($"tool '{tool.Name}' must not leak threadId into the LLM schema");
        }

        var bash = tools.OfType<AIFunction>().First(t => t.Name == "bash");
        bash.JsonSchema.GetProperty("properties").TryGetProperty("command", out _).ShouldBeTrue();
    }

    // -------------------------------------------------------------------------
    // 2. 真实 AgentExecutor 工具调用链 - mock chat client 发起 bash toolcall →
    //    沙箱真实执行 → 工具结果回到对话
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AgentExecutor_BashToolCall_ExecutesOnSandbox_AndRoundTripsResult()
    {
        var threadId = Guid.NewGuid();
        var accessor = new AgentExecutionContextAccessor();
        var serviceProvider = BuildServiceProvider(accessor);
        var tools = await ResolveSandboxToolsAsync(serviceProvider);

        var recordingSandbox = new RecordingSandbox();
        var chatClient = new ScriptedChatClient();
        var executor = new AgentExecutor(chatClient, new AgentExecutorOptions
        {
            Name = "sandbox-integration",
            Tools = tools.ToList()
        });

        var middleware = new SandboxMiddleware(
            new FixedSandboxProvider(recordingSandbox),
            Microsoft.Extensions.Options.Options.Create(new SandboxModuleOptions()),
            accessor,
            NullLogger<SandboxMiddleware>.Instance);

        var context = TestHelpers.CreateMinimalContext(threadId: threadId);
        context.Properties[SandboxPropertyKeys.ThreadData] = new ThreadDataState(
            ThreadDirectory: _workDir,
            WorkspacePath: Path.Combine(_workDir, "workspace"),
            UploadsPath: Path.Combine(_workDir, "uploads"),
            OutputsPath: Path.Combine(_workDir, "outputs"),
            SkillsPath: Path.Combine(_workDir, "skills"));

        var result = await middleware.InvokeAsync(context, async (ctx, ct) =>
        {
            var response = await executor.ExecuteAsync([new ChatMessage(ChatRole.User, "run it")], ct);
            return new AgentRunResult { Response = response.Text ?? string.Empty };
        });

        // 沙箱真实收到了 LLM toolcall 的命令
        recordingSandbox.ExecutedCommands.ShouldHaveSingleItem();
        recordingSandbox.ExecutedCommands[0].ShouldContain("echo integration");

        // 工具结果回到对话 - 第二次 LLM 请求携带匹配 CallId 的 FunctionResultContent
        chatClient.Requests.Count.ShouldBe(2);
        var functionResults = chatClient.Requests[1]
            .SelectMany(m => m.Contents)
            .OfType<FunctionResultContent>()
            .ToList();
        functionResults.ShouldHaveSingleItem();
        functionResults[0].CallId.ShouldBe("call-1");
        functionResults[0].Result!.ToString()!.ShouldContain("sandbox-exec-ok");

        // 最终回复回传调用方
        result.Response.ShouldBe("done");
    }

    // -------------------------------------------------------------------------
    // 3. 环境缺失（无中间件 - 子代理裸执行器/中间件未运行路径）→
    //    结构化错误回到对话，整条链不崩溃
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AgentExecutor_WithoutSandboxEnvironment_ReturnsStructuredErrorToConversation()
    {
        var serviceProvider = BuildServiceProvider();
        var tools = await ResolveSandboxToolsAsync(serviceProvider);

        var chatClient = new ScriptedChatClient();
        var executor = new AgentExecutor(chatClient, new AgentExecutorOptions
        {
            Name = "sandbox-integration",
            Tools = tools.ToList()
        });

        // 不经过 SandboxMiddleware，也没有发布任何环境
        var response = await executor.ExecuteAsync([new ChatMessage(ChatRole.User, "run it")]);

        response.Text.ShouldBe("done");

        var functionResults = chatClient.Requests[1]
            .SelectMany(m => m.Contents)
            .OfType<FunctionResultContent>()
            .ToList();
        functionResults.ShouldHaveSingleItem();
        functionResults[0].Result!.ToString()!.ShouldContain("Sandbox is not available");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static List<ToolDefinition> ScanSandboxTools()
    {
        var scanner = new ToolScanner(NullLogger<ToolScanner>.Instance);
        return scanner.ScanAssembly(typeof(SandboxTools).Assembly).ToList();
    }

    private ServiceProvider BuildServiceProvider(AgentExecutionContextAccessor? accessor = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IVirtualPathTranslator>(_translator);
        services.AddSingleton<IAgentExecutionContextAccessor>(accessor ?? new AgentExecutionContextAccessor());
        services.AddScoped<SandboxTools>();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// 经真实 ToolScanner + ToolRegistry + ToolResolver（内部 ToolAdapter）解析 sandbox 工具组。
    /// </summary>
    private static async Task<IList<AITool>> ResolveSandboxToolsAsync(IServiceProvider serviceProvider)
    {
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        foreach (var definition in ScanSandboxTools())
        {
            registry.Register(definition);
        }

        var mcpProvider = new Mock<IMcpToolProvider>();
        mcpProvider.Setup(p => p.GetToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AITool>());

        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.None));
        var openApiGenerator = new OpenApiToolGenerator(
            new Mock<IHttpClientFactory>().Object,
            new StaticOptionsMonitor<AIOptions>(new AIOptions()),
            NullLogger<OpenApiToolGenerator>.Instance);

        var resolver = new ToolResolver(
            registry,
            mcpProvider.Object,
            openApiGenerator,
            new StaticOptionsMonitor(new AIOptions()),
            serviceProvider,
            loggerFactory,
            loggerFactory.CreateLogger<ToolResolver>());

        var tools = await resolver.ResolveToolsAsync(["sandbox"]);
        tools.ShouldNotBeNull("the 'sandbox' tool group must resolve to AI tools");
        return tools!;
    }
}

/// <summary>记录所有执行命令的假沙箱 - 验证工具调用真实抵达沙箱。</summary>
file sealed class RecordingSandbox : ISandbox
{
    public List<string> ExecutedCommands { get; } = [];

    public string Id => "recording-sandbox";

    public Task<CommandResult> ExecuteCommandAsync(string command, CancellationToken ct = default)
    {
        ExecutedCommands.Add(command);
        return Task.FromResult(new CommandResult(0, "sandbox-exec-ok", string.Empty));
    }

    public Task<string> ReadFileAsync(string path, int? offset = null, int? limit = null, CancellationToken ct = default)
        => Task.FromResult(string.Empty);

    public Task WriteFileAsync(string path, string content, bool append = false, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task UpdateFileAsync(string path, byte[] content, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<FileEntry>> ListDirectoryAsync(string path, int maxDepth = 2, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<FileEntry>>([]);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>始终返回固定沙箱实例的假 Provider。</summary>
file sealed class FixedSandboxProvider(ISandbox sandbox) : ISandboxProvider
{
    public string Name => "fixed";

    public Task<ISandbox> CreateAsync(SandboxCreateOptions options, CancellationToken ct = default)
        => Task.FromResult(sandbox);
}

/// <summary>
/// 脚本化 chat client - 第一次调用返回对 bash 的 toolcall，第二次返回最终文本。
/// 记录每次请求的消息快照供断言。
/// </summary>
file sealed class ScriptedChatClient : IChatClient
{
    public List<List<ChatMessage>> Requests { get; } = [];

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(messages.ToList());

        if (Requests.Count == 1)
        {
            var toolCall = new FunctionCallContent("call-1", "bash",
                new Dictionary<string, object?> { ["command"] = "echo integration" });
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, [toolCall])]));
        }

        return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "done")]));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Streaming is not used by these tests");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

/// <summary>固定值 IOptionsMonitor。</summary>
file sealed class StaticOptionsMonitor(AIOptions value) : IOptionsMonitor<AIOptions>
{
    public AIOptions CurrentValue { get; } = value;

    public AIOptions Get(string? name) => CurrentValue;

    public IDisposable OnChange(Action<AIOptions, string?> listener) => new NoopDisposable();

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
