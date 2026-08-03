using Tnzi.AI.Mcp.Server;
using McpServerOptions = Tnzi.AI.Mcp.Options.McpServerOptions;

namespace Tnzi.AI.Tests.Mcp;

/// <summary>
/// McpServerHost 工具列表缓存测试 - 验证 ListToolsAsync 不再每次对每个 Agent 重建
/// scope + GetByIdAsync（N+1）；缓存命中复用，expose/remove 主动失效后才重建。
/// </summary>
public class McpServerHostToolCacheTests
{
    private readonly Mock<IAgentService> _agentService = new();
    private readonly IServiceProvider _serviceProvider;

    public McpServerHostToolCacheTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => _agentService.Object);
        _serviceProvider = services.BuildServiceProvider();
    }

    private McpServerHost CreateHost() => new(
        _serviceProvider,
        new StaticOptionsMonitor<McpServerOptions>(new McpServerOptions { Enabled = true, RequireAuthentication = false }),
        NullLogger<McpServerHost>.Instance,
        new McpServerSecurityMiddleware(
            new StaticOptionsMonitor<McpServerOptions>(new McpServerOptions()),
            NullLogger<McpServerSecurityMiddleware>.Instance,
            new ServiceCollection().BuildServiceProvider()),
        httpContextAccessor: null);

    private void SetupAgent(Guid id, string name)
    {
        _agentService.Setup(s => s.GetByIdAsync(id))
            .ReturnsAsync(Result<AgentDto>.Success(new AgentDto { Id = id, Name = name }));
    }

    [Fact]
    public async Task ListToolsAsync_RepeatedCalls_BuildAgentToolOnce()
    {
        var agentId = Guid.NewGuid();
        SetupAgent(agentId, "agent-a");
        var host = CreateHost();
        host.ExposeAgent(agentId);

        var first = await host.ListToolsAsync();
        await host.ListToolsAsync();
        await host.ListToolsAsync();

        first.Select(t => t.Name).ShouldContain("agent-a");
        // 缓存命中：三次 ListTools 只查库一次
        _agentService.Verify(s => s.GetByIdAsync(agentId), Times.Once);
    }

    [Fact]
    public async Task ExposeAgent_InvalidatesCache_ForcesRebuild()
    {
        var agentId = Guid.NewGuid();
        SetupAgent(agentId, "agent-a");
        var host = CreateHost();
        host.ExposeAgent(agentId);

        await host.ListToolsAsync();   // build #1
        await host.ListToolsAsync();   // cached
        _agentService.Verify(s => s.GetByIdAsync(agentId), Times.Once);

        // re-expose bumps the registration version → next call rebuilds
        host.ExposeAgent(agentId);
        await host.ListToolsAsync();   // build #2

        _agentService.Verify(s => s.GetByIdAsync(agentId), Times.Exactly(2));
    }

    [Fact]
    public async Task RemoveAgent_InvalidatesCache_AndDropsTool()
    {
        var agentId = Guid.NewGuid();
        SetupAgent(agentId, "agent-a");
        var host = CreateHost();
        host.ExposeAgent(agentId);

        (await host.ListToolsAsync()).Select(t => t.Name).ShouldContain("agent-a");

        host.RemoveAgent(agentId).ShouldBeTrue();

        // After removal the cache is invalidated and the tool no longer appears
        (await host.ListToolsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task ExposeTool_InvalidatesCache_NewToolAppearsWithoutFullAgentRebuild()
    {
        var agentId = Guid.NewGuid();
        SetupAgent(agentId, "agent-a");
        var host = CreateHost();
        host.ExposeAgent(agentId);

        (await host.ListToolsAsync()).Select(t => t.Name).ShouldContain("agent-a");
        _agentService.Verify(s => s.GetByIdAsync(agentId), Times.Once);

        // Registering a custom tool invalidates the cache; the agent tool is rebuilt
        // (one extra GetByIdAsync) but the new custom tool now shows up.
        host.ExposeTool("echo", "Echo", payload => Task.FromResult(payload.GetRawText()));

        var tools = await host.ListToolsAsync();
        tools.Select(t => t.Name).ShouldContain("echo");
        tools.Select(t => t.Name).ShouldContain("agent-a");
        _agentService.Verify(s => s.GetByIdAsync(agentId), Times.Exactly(2));
    }
}
