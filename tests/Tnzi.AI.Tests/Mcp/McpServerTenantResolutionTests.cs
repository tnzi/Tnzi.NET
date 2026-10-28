using Microsoft.AspNetCore.Http;
using Tnzi.AI.Mcp.Server;
using McpServerOptions = Tnzi.AI.Mcp.Options.McpServerOptions;

namespace Tnzi.AI.Tests.Mcp;

/// <summary>
/// MCP 工具的列出、按名解析与执行都在调用方的<b>有效租户</b>下进行：运行范围凭据 = 凭据租户，静态 API Key / 进程内 = 宿主。
/// </summary>
/// <remarks>
/// ★ 此前只有执行这一步对运行范围凭据切换租户；列出与按名解析在无租户的根作用域里查 Agent（多租户实体），
/// 凭据所属租户的 agent 一律「not found」被跳过，tools/list 为空、tools/call 答 not exposed，凭据对租户 agent 从来不可用。
/// 构建结果又是一份不分租户的单例缓存：在谁的上下文里建（管理员打开管理页 = 他的 JWT 租户；匿名请求带 <c>X-Tenant-Id</c>
/// = 头里的租户），就把谁看得见的 agent 名称与描述发给此后 30 秒内的所有调用方。静态 key 不切换租户，于是执行也跟着请求头走。
/// 这里的 Agent 服务替身按环境租户严格等值地可见（与全局过滤器同一语义）。
/// </remarks>
public class McpServerTenantResolutionTests
{
    private static readonly Guid TenantAgent = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid HostAgent = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000b");
    private static readonly Guid TenantX = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000a");
    private static readonly Guid TenantY = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

    private readonly Mock<IAgentService> _agentService = new();
    private readonly Mock<IAgentRuntime> _runtime = new();
    private readonly SwitchableCurrentTenant _tenant = new();
    private readonly HttpContextAccessorStub _accessor = new();
    private readonly List<Guid?> _runTenants = [];
    private readonly McpServerHost _host;

    public McpServerTenantResolutionTests()
    {
        // 租户 X 的 agent 只在租户 X 下可见；宿主 agent 只在宿主（null）下可见
        _agentService
            .Setup(s => s.GetByIdAsync(TenantAgent))
            .ReturnsAsync(() => _tenant.Id == TenantX
                ? Result<AgentDto>.Success(new AgentDto { Id = TenantAgent, Name = "tenant-agent", Description = "X only" })
                : Result<AgentDto>.Failure("Agent not found", 404));
        _agentService
            .Setup(s => s.GetByIdAsync(HostAgent))
            .ReturnsAsync(() => _tenant.Id is null
                ? Result<AgentDto>.Success(new AgentDto { Id = HostAgent, Name = "host-agent", Description = "host only" })
                : Result<AgentDto>.Failure("Agent not found", 404));
        _runtime
            .Setup(r => r.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .Returns<AgentRunRequest, CancellationToken>((_, _) =>
            {
                _runTenants.Add(_tenant.Id);
                return Task.FromResult(new AgentRunResult { Response = "ok" });
            });

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

        _host.ExposeAgent(TenantAgent);
        _host.ExposeAgent(HostAgent);
    }

    private void ActAsRunScoped(Guid tenantId)
    {
        var context = new DefaultHttpContext();
        context.Items[McpServerSecurityMiddleware.CallerHashItemKey] = $"run-caller-{tenantId}";
        context.Items[McpServerSecurityMiddleware.CallerScopeItemKey] = new McpCallerScope(new RunScopedCredential
        {
            RunId = Guid.NewGuid(),
            AgentId = TenantAgent,
            TenantId = tenantId,
            AllowedToolNames = ["*"]
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

    private async Task<List<string>> ListNamesAsync() => (await _host.ListToolsAsync()).Select(t => t.Name).ToList();

    [Fact]
    public async Task RunCredential_ListsAndCallsTheAgentsOfItsOwnTenant()
    {
        ActAsRunScoped(TenantX);

        var names = await ListNamesAsync();
        var call = await _host.CallToolAsync("tenant-agent", null);

        names.ShouldBe(["tenant-agent"]);
        call.IsError.ShouldNotBe(true);
        _runTenants.ShouldBe([TenantX]);
    }

    [Fact]
    public async Task RunCredential_ColdCache_CallResolvesTheTenantAgent()
    {
        // 没有先 tools/list：按名解析现建工具集，同样要在凭据租户下建
        ActAsRunScoped(TenantX);

        var call = await _host.CallToolAsync("tenant-agent", null);

        call.IsError.ShouldNotBe(true);
        _runTenants.ShouldBe([TenantX]);
    }

    [Fact]
    public async Task ToolListBuiltForOneTenant_IsNeverServedToAnother()
    {
        ActAsRunScoped(TenantX);
        (await ListNamesAsync()).ShouldBe(["tenant-agent"]);

        ActAsRunScoped(TenantY);
        var namesForY = await ListNamesAsync();
        var callFromY = await _host.CallToolAsync("tenant-agent", null);

        namesForY.ShouldBeEmpty();
        callFromY.IsError.ShouldBe(true);
        _runTenants.ShouldBeEmpty();
    }

    [Fact]
    public async Task InProcessListingUnderAnAmbientTenant_DoesNotSeedWhatStaticKeyCallersSee()
    {
        // 租户 X 的管理员打开 MCP 管理页：进程内调用，环境租户是他的 JWT 租户
        _accessor.HttpContext = null;
        using (_tenant.Change(TenantX))
        {
            (await ListNamesAsync()).ShouldNotContain("tenant-agent");
        }

        ActAsStaticKey();
        var names = await ListNamesAsync();

        names.ShouldBe(["host-agent"]);
    }

    [Fact]
    public async Task StaticKey_UnderAHeaderChosenTenant_StillRunsInTheHostScope()
    {
        // TenantResolverMiddleware 按匿名请求的 X-Tenant-Id 头切了环境租户：那是客户端可控的输入，不是调用方身份
        ActAsStaticKey();
        using (_tenant.Change(TenantX))
        {
            var names = await ListNamesAsync();
            var call = await _host.CallToolAsync("host-agent", null);
            var crossTenant = await _host.CallToolAsync("tenant-agent", null);

            names.ShouldBe(["host-agent"]);
            call.IsError.ShouldNotBe(true);
            crossTenant.IsError.ShouldBe(true);
        }

        _runTenants.ShouldBe([(Guid?)null]);
    }

    [Fact]
    public async Task CustomTool_RunsInTheCallersEffectiveTenant()
    {
        Guid? seen = Guid.Empty;
        _host.ExposeTool("whoami", "reports the tenant", _ =>
        {
            seen = _tenant.Id;
            return Task.FromResult("ok");
        });
        ActAsRunScoped(TenantX);

        (await _host.CallToolAsync("whoami", null)).IsError.ShouldNotBe(true);

        seen.ShouldBe(TenantX);
    }

    private sealed class HttpContextAccessorStub : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class SwitchableCurrentTenant : ICurrentTenant
    {
        private readonly AsyncLocal<Guid?> _current = new();

        public Guid? Id => _current.Value;
        public string? Name => null;
        public bool IsAvailable => Id.HasValue;

        public IDisposable Change(Guid? tenantId, string? tenantName = null)
        {
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
