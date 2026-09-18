using System.Reflection;
using Tnzi.AI.Tools.Models;

namespace Tnzi.AI.Tests;

/// <summary>
/// <see cref="SubAgentOptions"/> 的八个属性里此前只有 MaxDepth / MaxDescendantsPerRoot 真有读者：
/// 管理端可热改的 Enabled / MaxConcurrentSubAgents、TimeoutSeconds 与三份工具名单都没有任何运行时消费，
/// 而唯一自称消费它们的 <c>SubAgentLimitMiddleware</c> 在<b>历史消息</b>里找一个叫 <c>task</c> 的工具调用（不存在这个工具名）。
/// 现在：Enabled / MaxConcurrent / Timeout 在 <c>SubAgentExecutionService.SpawnAsync</c> 生效，
/// 三份名单在 <c>ToolResolver</c> 合并之后统一生效（C# / OpenAPI / MCP 工具一视同仁）。
/// </summary>
public class SubAgentOptionsEnforcementTests
{
    // ---------------------------------------------------------------------
    // 工具名单（ToolResolver）
    // ---------------------------------------------------------------------

    [Fact]
    public async Task GlobalDisallowedTools_RemovesTheToolForEveryone()
    {
        var resolver = CreateResolver(o => o.GlobalDisallowedTools = ["delete_file"], accessor: new AgentExecutionContextAccessor());

        var tools = await resolver.ResolveToolsAsync(["files"]);

        tools.ShouldNotBeNull();
        tools.Select(t => t.Name).ShouldBe(["read_file", "write_file"], ignoreOrder: true);
    }

    [Fact]
    public async Task SubAgentDisallowedTools_OnlyAppliesToSubAgents()
    {
        var mainAgent = new AgentExecutionContextAccessor();
        _ = mainAgent.Properties;
        var mainResolver = CreateResolver(o => o.SubAgentDisallowedTools = ["write_file"], accessor: mainAgent);
        (await mainResolver.ResolveToolsAsync(["files"]))!.Select(t => t.Name).ShouldContain("write_file");

        var subAgent = new AgentExecutionContextAccessor();
        subAgent.Properties[ContextPropertyKeys.IsSubAgent] = true;
        var subResolver = CreateResolver(o => o.SubAgentDisallowedTools = ["write_file"], accessor: subAgent);
        (await subResolver.ResolveToolsAsync(["files"]))!.Select(t => t.Name).ShouldNotContain("write_file");
    }

    [Fact]
    public async Task AsyncAgentAllowedTools_WhitelistsBackgroundRunsOnly()
    {
        var foreground = new AgentExecutionContextAccessor { CurrentRequest = new AgentRunRequest { UserMessage = "hi" } };
        var fgResolver = CreateResolver(o => o.AsyncAgentAllowedTools = ["read_file"], accessor: foreground);
        (await fgResolver.ResolveToolsAsync(["files"]))!.Count.ShouldBe(3);

        var background = new AgentExecutionContextAccessor
        {
            CurrentRequest = new AgentRunRequest { UserMessage = "hi", ParentRunId = Guid.NewGuid() }
        };
        var bgResolver = CreateResolver(o => o.AsyncAgentAllowedTools = ["read_file"], accessor: background);
        (await bgResolver.ResolveToolsAsync(["files"]))!.Select(t => t.Name).ShouldBe(["read_file"]);
    }

    [Fact]
    public async Task AsyncAgentAllowedTools_AppliesToRootBackgroundRuns()
    {
        // 根 spawn（管理端端点、未开追踪的聊天里的 spawn_agent）没有 ParentRunId，但它就是后台运行
        var background = new AgentExecutionContextAccessor
        {
            CurrentRequest = new AgentRunRequest { UserMessage = "hi", IsBackground = true }
        };
        var resolver = CreateResolver(o => o.AsyncAgentAllowedTools = ["read_file"], accessor: background);

        (await resolver.ResolveToolsAsync(["files"]))!.Select(t => t.Name).ShouldBe(["read_file"]);
    }

    [Fact]
    public async Task AsyncAgentAllowedTools_Empty_DoesNotRestrictBackgroundRuns()
    {
        var background = new AgentExecutionContextAccessor
        {
            CurrentRequest = new AgentRunRequest { UserMessage = "hi", ParentRunId = Guid.NewGuid() }
        };
        var resolver = CreateResolver(o => o.AsyncAgentAllowedTools = [], accessor: background);

        (await resolver.ResolveToolsAsync(["files"]))!.Count.ShouldBe(3);
    }

    [Fact]
    public async Task ToolLists_ApplyToMcpToolsToo()
    {
        // 名单在合并之后生效，不是只过滤 C# 工具：MCP 工具同名照样出局
        var mcp = new Mock<IMcpToolProvider>();
        mcp.Setup(p => p.GetToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([NamedTool("mcp_shell"), NamedTool("mcp_search")]);
        var resolver = CreateResolver(o => o.GlobalDisallowedTools = ["mcp_shell"], accessor: new AgentExecutionContextAccessor(),
            mcp: mcp.Object, mcpEnabled: true);

        var tools = await resolver.ResolveToolsAsync(null);

        tools!.Select(t => t.Name).ShouldBe(["mcp_search"]);
    }

    // ---------------------------------------------------------------------
    // 读者门禁：每个 [RuntimeSetting] 属性都得有 Options/ 之外的读者
    // ---------------------------------------------------------------------

    [Fact]
    public void EveryRuntimeSettingOnSubAgentOptions_HasAReaderOutsideTheOptionsFolder()
    {
        // 管理端把这些字段渲染成可热改的开关；没有读者的字段 = 改了 200、什么都没变、没有任何症状
        var sources = ModuleSourcesOutsideOptions();

        var settings = typeof(SubAgentOptions).GetProperties()
            .Where(p => p.GetCustomAttribute<RuntimeSettingAttribute>() != null)
            .Select(p => p.Name)
            .ToList();
        settings.ShouldNotBeEmpty();

        var unread = settings.Where(name => !sources.Any(src => src.Contains("." + name, StringComparison.Ordinal))).ToList();
        unread.ShouldBeEmpty($"SubAgentOptions properties with no runtime reader: {string.Join(", ", unread)}");
    }

    [Fact]
    public void ToolListsOnSubAgentOptions_HaveAReaderOutsideTheOptionsFolder()
    {
        var sources = ModuleSourcesOutsideOptions();

        foreach (var name in new[] { nameof(SubAgentOptions.GlobalDisallowedTools), nameof(SubAgentOptions.SubAgentDisallowedTools), nameof(SubAgentOptions.AsyncAgentAllowedTools) })
        {
            sources.Any(src => src.Contains("." + name, StringComparison.Ordinal)).ShouldBeTrue($"{name} has no reader");
        }
    }

    /// <summary>src/Tnzi.AI 里 Options/ 之外的全部源码（经 RepoScan：不跟 junction/symlink，不会够到 pnpm 工作区）。</summary>
    private static List<string> ModuleSourcesOutsideOptions()
    {
        var sources = RepoScan.EnumerateFiles("src/Tnzi.AI", "*.cs")
            .Where(f => !f.Replace('\\', '/').Contains("/Options/", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToList();
        sources.ShouldNotBeEmpty();
        return sources;
    }

    // ---------------------------------------------------------------------

    private sealed class FileTools
    {
        public string ReadFile() => "read";
        public string WriteFile() => "write";
        public string DeleteFile() => "delete";
    }

    private static ToolDefinition Def(string name, string method) => new()
    {
        Name = name,
        GroupName = "files",
        ProviderType = typeof(FileTools),
        MethodInfo = typeof(FileTools).GetMethod(method)!,
        RequiredPermissions = []
    };

    private static AITool NamedTool(string name)
    {
        var mock = new Mock<AITool>();
        mock.Setup(t => t.Name).Returns(name);
        return mock.Object;
    }

    private static ToolResolver CreateResolver(
        Action<SubAgentOptions> configure,
        IAgentExecutionContextAccessor accessor,
        IMcpToolProvider? mcp = null,
        bool mcpEnabled = false)
    {
        var registry = new Mock<IToolRegistry>();
        registry.Setup(r => r.GetToolsByGroupsWithPermissions(It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>?>()))
            .Returns([Def("read_file", nameof(FileTools.ReadFile)), Def("write_file", nameof(FileTools.WriteFile)), Def("delete_file", nameof(FileTools.DeleteFile))]);

        IMcpToolProvider mcpProvider;
        if (mcp is not null)
        {
            mcpProvider = mcp;
        }
        else
        {
            var emptyMcp = new Mock<IMcpToolProvider>();
            emptyMcp.Setup(p => p.GetToolsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<AITool>());
            mcpProvider = emptyMcp.Object;
        }

        var aiOptions = new StaticOptionsMonitor<AIOptions>(new AIOptions { Mcp = new McpOptions { Enabled = mcpEnabled } });
        var subAgentOptions = new SubAgentOptions();
        configure(subAgentOptions);
        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.None));
        var services = new ServiceCollection().AddSingleton<FileTools>().BuildServiceProvider();

        return new ToolResolver(
            registry.Object,
            mcpProvider,
            new OpenApiToolGenerator(Mock.Of<IHttpClientFactory>(), aiOptions, loggerFactory.CreateLogger<OpenApiToolGenerator>()),
            aiOptions,
            services,
            loggerFactory,
            loggerFactory.CreateLogger<ToolResolver>(),
            executionContextAccessor: accessor,
            subAgentOptions: new StaticOptionsMonitor<SubAgentOptions>(subAgentOptions));
    }
}
