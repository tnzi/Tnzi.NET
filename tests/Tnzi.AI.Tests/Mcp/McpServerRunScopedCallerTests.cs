using Microsoft.AspNetCore.Http;
using Tnzi.AI.Mcp.Server;
using McpServerOptions = Tnzi.AI.Mcp.Options.McpServerOptions;

namespace Tnzi.AI.Tests.Mcp;

/// <summary>
/// 运行范围凭据的<b>调用面</b>必须真的比静态 API Key 窄。
/// </summary>
/// <remarks>
/// ★ 此前 <c>ValidateCallerAsync</c> 校验完凭据就把它丢了：AgentId / TenantId 只出现在一行 Debug 日志里，
/// 之后调用方与一把静态 key 无从分辨 —— <c>tools/list</c> 列出全部、<c>tools/call</c> 派发任意 agent 与任意
/// 消费方自定义工具，agent 在根作用域无租户上下文运行。契约、注释、文档却都写着「上限是该 Agent 自身的权限」。
/// 凭据交到一个以 <c>--permission-mode bypassPermissions</c> 跑任意代码的子进程手里，被注入提示就等于拿到全部面。
/// </remarks>
public class McpServerRunScopedCallerTests
{
    private static readonly Guid AgentA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TenantX = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private readonly Mock<IAgentService> _agentService = new();
    private readonly Mock<IAgentRuntime> _runtime = new();
    private readonly RecordingCurrentTenant _tenant = new();
    private readonly HttpContextAccessorStub _accessor = new();
    private readonly McpServerHost _host;

    public McpServerRunScopedCallerTests()
    {
        _agentService
            .Setup(s => s.GetByIdAsync(AgentA))
            .ReturnsAsync(Result<AgentDto>.Success(new AgentDto { Id = AgentA, Name = "agent-a", Description = "A" }));
        _runtime
            .Setup(r => r.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .Returns<AgentRunRequest, CancellationToken>((_, _) =>
                Task.FromResult(new AgentRunResult { Response = $"tenant={_tenant.Id?.ToString() ?? "none"}" }));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => _agentService.Object);
        services.AddScoped(_ => _runtime.Object);
        services.AddSingleton<ICurrentTenant>(_tenant);
        var serviceProvider = services.BuildServiceProvider();

        var security = new McpServerSecurityMiddleware(
            new StaticOptionsMonitor<McpServerOptions>(new McpServerOptions { RateLimitPerMinute = 0, EnableAuditLog = false, EnableToolAnalytics = false }),
            NullLogger<McpServerSecurityMiddleware>.Instance,
            new ServiceCollection().BuildServiceProvider());

        _host = new McpServerHost(
            serviceProvider,
            new StaticOptionsMonitor<McpServerOptions>(new McpServerOptions { Enabled = true, RequireAuthentication = false }),
            NullLogger<McpServerHost>.Instance,
            security,
            _accessor);

        _host.ExposeAgent(AgentA);
        _host.ExposeTool("create_ticket", "creates a ticket", _ => Task.FromResult("ticket-1"));
        _host.ExposeTool("delete_everything", "dangerous", _ => Task.FromResult("gone"));
    }

    private void ActAsRunScoped(params string[] allowedTools)
    {
        var context = new DefaultHttpContext();
        context.Items[McpServerSecurityMiddleware.CallerHashItemKey] = "run-caller";
        context.Items[McpServerSecurityMiddleware.CallerScopeItemKey] = new McpCallerScope(new RunScopedCredential
        {
            RunId = Guid.NewGuid(),
            AgentId = AgentA,
            TenantId = TenantX,
            AllowedToolNames = allowedTools
        });
        _accessor.HttpContext = context;
    }

    private void ActAsStaticKey()
    {
        var context = new DefaultHttpContext();
        context.Items[McpServerSecurityMiddleware.CallerHashItemKey] = "static-caller";
        context.Items[McpServerSecurityMiddleware.CallerScopeItemKey] = McpCallerScope.Unrestricted;
        _accessor.HttpContext = context;
    }

    [Fact]
    public async Task CallTool_RunCredential_ToolOutsideAllowList_ReturnsMcpError()
    {
        ActAsRunScoped("create_ticket");

        var agentCall = await _host.CallToolAsync("agent-a", null);
        var customCall = await _host.CallToolAsync("delete_everything", null);

        agentCall.IsError.ShouldBe(true);
        customCall.IsError.ShouldBe(true);
        _runtime.Verify(r => r.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CallTool_RunCredential_ToolInsideAllowList_Executes()
    {
        ActAsRunScoped("create_ticket");

        var result = await _host.CallToolAsync("create_ticket", null);

        result.IsError.ShouldNotBe(true);
    }

    [Fact]
    public async Task ListTools_RunCredential_FiltersToAllowList()
    {
        ActAsRunScoped("create_ticket");

        var tools = await _host.ListToolsAsync();

        tools.Select(t => t.Name).ShouldBe(["create_ticket"]);
    }

    [Fact]
    public async Task ListTools_RunCredential_EmptyAllowList_ListsNothing()
    {
        ActAsRunScoped();

        (await _host.ListToolsAsync()).ShouldBeEmpty();
        (await _host.CallToolAsync("create_ticket", null)).IsError.ShouldBe(true);
    }

    [Fact]
    public async Task ListTools_RunCredential_Wildcard_ListsEverything()
    {
        ActAsRunScoped("*");

        var tools = await _host.ListToolsAsync();

        tools.Select(t => t.Name).ShouldBe(["agent-a", "create_ticket", "delete_everything"], ignoreOrder: true);
    }

    [Fact]
    public async Task CallTool_StaticApiKey_Unrestricted()
    {
        ActAsStaticKey();

        var tools = await _host.ListToolsAsync();
        var result = await _host.CallToolAsync("delete_everything", null);

        tools.Count.ShouldBe(3);
        result.IsError.ShouldNotBe(true);
    }

    [Fact]
    public async Task InvokeAgent_RunCredential_RunsUnderCredentialTenant()
    {
        ActAsRunScoped("agent-a");

        var result = await _host.CallToolAsync("agent-a", null);

        result.IsError.ShouldNotBe(true);
        _tenant.Changes.ShouldContain(TenantX);
        _tenant.Id.ShouldBeNull("tenant context must be restored after the call");
    }

    [Fact]
    public async Task InvokeAgent_StaticApiKey_NeverTouchesTenantContext()
    {
        ActAsStaticKey();

        await _host.CallToolAsync("agent-a", null);

        _tenant.Changes.ShouldBeEmpty();
    }

    private sealed class HttpContextAccessorStub : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class RecordingCurrentTenant : ICurrentTenant
    {
        private readonly AsyncLocal<Guid?> _current = new();
        public ConcurrentQueue<Guid?> Changes { get; } = new();

        public Guid? Id => _current.Value;
        public string? Name => null;
        public bool IsAvailable => Id.HasValue;

        public IDisposable Change(Guid? tenantId, string? tenantName = null)
        {
            Changes.Enqueue(tenantId);
            var previous = _current.Value;
            _current.Value = tenantId;
            return new RestoreScope(() => _current.Value = previous);
        }

        private sealed class RestoreScope(Action restore) : IDisposable
        {
            public void Dispose() => restore();
        }
    }
}
