using ModelContextProtocol.Protocol;
using Tnzi.AI.Mcp.Server;
using McpServerOptions = Tnzi.AI.Mcp.Options.McpServerOptions;

namespace Tnzi.AI.Tests.Mcp;

/// <summary>
/// 暴露注册表的四条契约：管理端暴露先确认 Agent 存在且启用、移除配置项里的 Agent 不会被悄悄加回、
/// 工具名对任何 Agent 名都合规且互不撞名、自定义工具宣告的输入与 handler 收到的是同一个形状。
/// </summary>
public class McpServerHostExposureTests
{
    private readonly Mock<IAgentService> _agentService = new();
    private readonly Mock<IAgentRuntime> _runtime = new();
    private readonly List<Guid> _ranAgents = [];
    private readonly McpServerOptions _options = new() { Enabled = true, RequireAuthentication = false };
    private readonly McpServerHost _host;

    public McpServerHostExposureTests()
    {
        _agentService
            .Setup(s => s.GetByIdAsync(It.IsAny<Guid>()))
            .ReturnsAsync(Result<AgentDto>.Failure("Agent not found", 404));
        _runtime
            .Setup(r => r.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .Returns<AgentRunRequest, CancellationToken>((request, _) =>
            {
                _ranAgents.Add(request.AgentId!.Value);
                return Task.FromResult(new AgentRunResult { Response = "ok" });
            });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => _agentService.Object);
        services.AddScoped(_ => _runtime.Object);

        _host = new McpServerHost(
            services.BuildServiceProvider(),
            new StaticOptionsMonitor<McpServerOptions>(_options),
            NullLogger<McpServerHost>.Instance,
            new McpServerSecurityMiddleware(
                new StaticOptionsMonitor<McpServerOptions>(new McpServerOptions { RateLimitPerMinute = 0, EnableAuditLog = false, EnableToolAnalytics = false }),
                NullLogger<McpServerSecurityMiddleware>.Instance,
                new ServiceCollection().BuildServiceProvider()),
            httpContextAccessor: null);
    }

    private Guid Agent(string name, bool enabled = true)
    {
        var id = Guid.NewGuid();
        _agentService.Setup(s => s.GetByIdAsync(id))
            .ReturnsAsync(Result<AgentDto>.Success(new AgentDto { Id = id, Name = name, IsEnabled = enabled }));
        return id;
    }

    private async Task<List<string>> ToolNamesAsync() => [.. (await _host.ListToolsAsync()).Select(t => t.Name)];

    // ─── 管理端暴露先校验 ───────────────────────────────────────────────────

    [Fact]
    public async Task ExposeAgentAsync_AnUnknownAgent_Is404_AndIsNotRegistered()
    {
        var result = await _host.ExposeAgentAsync(Guid.NewGuid());

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
        _host.GetExposedAgentIds().ShouldBeEmpty("an agent nobody can see must not show up as exposed");
    }

    [Fact]
    public async Task ExposeAgentAsync_ADisabledAgent_Is400_AndIsNotRegistered()
    {
        var disabled = Agent("paused", enabled: false);

        var result = await _host.ExposeAgentAsync(disabled);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        _host.GetExposedAgentIds().ShouldBeEmpty();
    }

    [Fact]
    public async Task ExposeAgentAsync_AnInvalidToolName_Is400()
    {
        var agent = Agent("helper");

        var result = await _host.ExposeAgentAsync(agent, new McpToolExposureOptions { ToolName = "客服 助手" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        _host.GetExposedAgentIds().ShouldBeEmpty();
    }

    [Fact]
    public async Task ExposeAgentAsync_AVisibleEnabledAgent_IsListed()
    {
        var agent = Agent("helper");

        (await _host.ExposeAgentAsync(agent)).Succeeded.ShouldBeTrue();

        _host.GetExposedAgentIds().ShouldBe([agent]);
        (await ToolNamesAsync()).ShouldBe(["helper"]);
    }

    // ─── 配置项里的 Agent ──────────────────────────────────────────────────────

    [Fact]
    public async Task AConfiguredAgentRemovedByAnAdmin_StaysRemoved()
    {
        var configured = Agent("configured");
        _options.ExposedAgentIds = [configured];

        (await ToolNamesAsync()).ShouldContain("configured");
        _host.IsConfiguredAgent(configured).ShouldBeTrue();

        _host.RemoveAgent(configured).ShouldBeTrue();

        (await ToolNamesAsync()).ShouldNotContain("configured", "the next tools/list must not bring it back");
        var call = await _host.CallToolAsync("configured", new Dictionary<string, JsonElement>());
        call.IsError.ShouldBe(true);
        _host.GetExposedAgentIds().ShouldNotContain(configured);
        _ranAgents.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnAgentAddedToTheConfigurationAtRuntime_IsStillImported()
    {
        // 对照：只导入一次不等于再也不导入，热更新新增的配置项照常生效
        var first = Agent("first");
        _options.ExposedAgentIds = [first];
        await ToolNamesAsync();

        var second = Agent("second");
        _options.ExposedAgentIds = [first, second];

        (await ToolNamesAsync()).ShouldBe(["first", "second"], ignoreOrder: true);
    }

    // ─── 工具名 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnAgentWithANonAsciiName_GetsAValidToolName_AndIsCallable()
    {
        var agent = Agent("客服助手");
        _host.ExposeAgent(agent);

        var names = await ToolNamesAsync();

        names.Count.ShouldBe(1, "the SDK rejected non-ASCII names and the tool silently disappeared");
        names[0].ShouldMatch("^[A-Za-z0-9_-]{1,64}$");

        var call = await _host.CallToolAsync(names[0], Arguments("message", "hello"));
        call.IsError.ShouldBe(false);
        _ranAgents.ShouldBe([agent]);
    }

    [Fact]
    public async Task AVeryLongAgentName_IsCutTo64Characters()
    {
        _host.ExposeAgent(Agent(new string('a', 200)));

        (await ToolNamesAsync()).Single().Length.ShouldBe(64);
    }

    [Fact]
    public async Task TwoAgentsWhoseNamesSanitizeAlike_GetDistinctNames_AndEachReachesItsOwnAgent()
    {
        var spaced = Agent("Sales Bot");
        var underscored = Agent("Sales_Bot");
        _host.ExposeAgent(spaced);
        _host.ExposeAgent(underscored);

        var names = await ToolNamesAsync();

        names.Count.ShouldBe(2);
        names.Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(2);
        names.ShouldAllBe(n => n.StartsWith("Sales_Bot_"));

        foreach (var name in names)
        {
            (await _host.CallToolAsync(name, Arguments("message", "hi"))).IsError.ShouldBe(false);
        }

        _ranAgents.ShouldBe([spaced, underscored], ignoreOrder: true);
    }

    [Fact]
    public async Task AnAgentNamedLikeACustomTool_IsStillReachable()
    {
        var agent = Agent("echo");
        _host.ExposeAgent(agent);
        _host.ExposeTool("echo", "Echo", payload => Task.FromResult("custom"));

        var names = await ToolNamesAsync();
        var agentToolName = names.Single(n => n != "echo");

        (await _host.CallToolAsync("echo", Arguments("input", "x"))).Content.OfType<TextContentBlock>().Single().Text.ShouldBe("custom");
        (await _host.CallToolAsync(agentToolName, Arguments("message", "x"))).IsError.ShouldBe(false);
        _ranAgents.ShouldBe([agent]);
    }

    [Fact]
    public void ExposeTool_AnInvalidName_IsRejectedAtRegistration()
    {
        Should.Throw<ArgumentException>(() => _host.ExposeTool("工具", "x", _ => Task.FromResult("x")));
        Should.Throw<ArgumentException>(() => _host.ExposeAgent(Guid.NewGuid(), new McpToolExposureOptions { ToolName = "has space" }));
    }

    // ─── 自定义工具：宣告与运行时同一个形状 ─────────────────────────────────

    [Fact]
    public async Task ACustomToolWithoutASchema_DeclaresInput_AndTheHandlerReceivesThatObject()
    {
        JsonElement received = default;
        _host.ExposeTool("echo", "Echo", payload =>
        {
            received = payload.Clone();
            return Task.FromResult("done");
        });

        var tool = (await _host.ListToolsAsync()).Single();
        tool.InputSchema.GetProperty("properties").GetProperty("input").GetProperty("type").GetString().ShouldBe("string");
        tool.InputSchema.GetProperty("required")[0].GetString().ShouldBe("input");

        // 按宣告调用：客户端发的就是 {"input": ...}
        await _host.CallToolAsync("echo", Arguments("input", "hello"));

        received.GetProperty("input").GetString().ShouldBe("hello");
    }

    [Fact]
    public async Task ACustomToolWithADeclaredSchema_AdvertisesIt_AndTheHandlerReceivesThoseFields()
    {
        using var schema = JsonDocument.Parse(
            """{"type":"object","properties":{"title":{"type":"string"},"priority":{"type":"integer"}},"required":["title"]}""");
        JsonElement received = default;
        _host.ExposeTool("create_ticket", "Create a ticket", schema.RootElement, payload =>
        {
            received = payload.Clone();
            return Task.FromResult("created");
        });

        var tool = (await _host.ListToolsAsync()).Single();
        tool.InputSchema.GetProperty("properties").TryGetProperty("priority", out _).ShouldBeTrue();

        await _host.CallToolAsync("create_ticket", new Dictionary<string, JsonElement>
        {
            ["title"] = JsonSerializer.SerializeToElement("Printer is down"),
            ["priority"] = JsonSerializer.SerializeToElement(2)
        });

        received.GetProperty("title").GetString().ShouldBe("Printer is down");
        received.GetProperty("priority").GetInt32().ShouldBe(2);
    }

    [Fact]
    public void ExposeTool_ASchemaThatIsNotAnObject_IsRejectedAtRegistration()
    {
        using var schema = JsonDocument.Parse("""{"type":"string"}""");

        Should.Throw<ArgumentException>(() => _host.ExposeTool("bad", "x", schema.RootElement, _ => Task.FromResult("x")));
    }

    [Fact]
    public async Task AgentTools_DeclareTheMessageTheRuntimeReads()
    {
        _host.ExposeAgent(Agent("helper"));

        var tool = (await _host.ListToolsAsync()).Single();

        tool.InputSchema.GetProperty("required")[0].GetString().ShouldBe("message");
        (await _host.CallToolAsync("helper", Arguments("message", "hi"))).IsError.ShouldBe(false);
    }

    private static Dictionary<string, JsonElement> Arguments(string name, string value)
        => new() { [name] = JsonSerializer.SerializeToElement(value) };
}
