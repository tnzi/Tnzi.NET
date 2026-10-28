using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Protocol;
using Tnzi.AI.Mcp.Server;
using McpServerOptions = Tnzi.AI.Mcp.Options.McpServerOptions;

namespace Tnzi.AI.Tests.Mcp;

/// <summary>
/// 外部 CLI 经 MCP 回写通道调用时，平台把派发者自己连接的远程 MCP 服务器的工具转发出去：
/// CLI 只拿到工具名与结果，令牌留在平台；用户取自凭据里运行记录的派发者；必须由凭据显式点名
/// <see cref="RunScopedCredential.UserMcpTools"/>，<c>*</c> 不涵盖它。
/// </summary>
public class McpServerUserRelayTests
{
    private static readonly Guid Dispatcher = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
    private static readonly Guid AgentA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private readonly Mock<IMcpToolProvider> _provider = new();
    private readonly HttpContextAccessorStub _accessor = new();
    private readonly List<Guid> _requestedUsers = [];
    private readonly List<string> _invoked = [];
    private readonly McpServerHost _host;

    public McpServerUserRelayTests()
    {
        _provider
            .Setup(p => p.GetUserToolsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid user, CancellationToken _) =>
            {
                _requestedUsers.Add(user);
                return (IReadOnlyList<AITool>)new List<AITool>
                {
                    AIFunctionFactory.Create((string query) => Record("github__search", $"found {query}"), "github__search", "search github"),
                    AIFunctionFactory.Create(() => "Tool call rejected: approval handler is not configured.", "github__delete_repo", "delete"),
                    AIFunctionFactory.Create(() => Record("create_ticket", "shadow"), "create_ticket", "must not shadow the platform's")
                };
            });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_provider.Object);
        services.AddSingleton<ICurrentTenant>(new TenantStub());
        var security = new McpServerSecurityMiddleware(
            new StaticOptionsMonitor<McpServerOptions>(new McpServerOptions { RateLimitPerMinute = 0, EnableAuditLog = false, EnableToolAnalytics = false }),
            NullLogger<McpServerSecurityMiddleware>.Instance,
            new ServiceCollection().BuildServiceProvider());

        _host = new McpServerHost(
            services.BuildServiceProvider(),
            new StaticOptionsMonitor<McpServerOptions>(new McpServerOptions { Enabled = true, RequireAuthentication = false }),
            NullLogger<McpServerHost>.Instance,
            security,
            _accessor);

        _host.ExposeTool("create_ticket", "creates a ticket", _ => Task.FromResult("ticket-1"));
        _host.ExposeTool("delete_everything", "dangerous", _ => Task.FromResult("gone"));
    }

    private string Record(string tool, string result)
    {
        _invoked.Add(tool);
        return result;
    }

    private void ActAsRun(Guid? creator, params string[] allowedTools)
    {
        var context = new DefaultHttpContext();
        context.Items[McpServerSecurityMiddleware.CallerHashItemKey] = "run-caller";
        context.Items[McpServerSecurityMiddleware.CallerScopeItemKey] = new McpCallerScope(new RunScopedCredential
        {
            RunId = Guid.NewGuid(),
            AgentId = AgentA,
            CreatorId = creator,
            AllowedToolNames = allowedTools
        });
        _accessor.HttpContext = context;
    }

    private static string Text(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));

    [Fact]
    public async Task ListTools_WithUserMcpGrant_AddsTheDispatchersServerTools_PlatformNamesWin()
    {
        ActAsRun(Dispatcher, "create_ticket", RunScopedCredential.UserMcpTools);

        var tools = await _host.ListToolsAsync();

        tools.Select(t => t.Name).ShouldBe(["create_ticket", "github__search", "github__delete_repo"], ignoreOrder: true);
        tools.Single(t => t.Name == "create_ticket").Description.ShouldBe("creates a ticket");
        _requestedUsers.ShouldAllBe(u => u == Dispatcher);
    }

    [Fact]
    public async Task Wildcard_DoesNotCoverUserTools()
    {
        ActAsRun(Dispatcher, "*");

        var tools = await _host.ListToolsAsync();

        tools.Select(t => t.Name).ShouldBe(["create_ticket", "delete_everything"], ignoreOrder: true);
        (await _host.CallToolAsync("github__search", Args("query", "x"))).IsError.ShouldBe(true);
        _provider.Verify(p => p.GetUserToolsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WildcardPlusGrant_CoversBoth()
    {
        ActAsRun(Dispatcher, "*", RunScopedCredential.UserMcpTools);

        (await _host.ListToolsAsync()).Select(t => t.Name)
            .ShouldBe(["create_ticket", "delete_everything", "github__search", "github__delete_repo"], ignoreOrder: true);
    }

    [Fact]
    public async Task RunWithoutDispatcher_GetsNoUserTools_EvenWithTheGrant()
    {
        ActAsRun(null, "create_ticket", RunScopedCredential.UserMcpTools);

        (await _host.ListToolsAsync()).Select(t => t.Name).ShouldBe(["create_ticket"]);
        (await _host.CallToolAsync("github__search", Args("query", "x"))).IsError.ShouldBe(true);
        _provider.Verify(p => p.GetUserToolsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StaticApiKey_NeverGetsUserTools()
    {
        var context = new DefaultHttpContext();
        context.Items[McpServerSecurityMiddleware.CallerHashItemKey] = "static";
        context.Items[McpServerSecurityMiddleware.CallerScopeItemKey] = McpCallerScope.Unrestricted;
        _accessor.HttpContext = context;

        (await _host.ListToolsAsync()).Select(t => t.Name).ShouldBe(["create_ticket", "delete_everything"], ignoreOrder: true);
        (await _host.CallToolAsync("github__search", Args("query", "x"))).IsError.ShouldBe(true);
        _provider.Verify(p => p.GetUserToolsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CallTool_RelaysToTheUsersServer_AndReturnsOnlyTheResult()
    {
        ActAsRun(Dispatcher, RunScopedCredential.UserMcpTools);

        var result = await _host.CallToolAsync("github__search", Args("query", "tnzi"));

        result.IsError.ShouldNotBe(true);
        Text(result).ShouldContain("found tnzi");
        _invoked.ShouldBe(["github__search"]);
        _requestedUsers.ShouldAllBe(u => u == Dispatcher);
    }

    [Fact]
    public async Task GrantOnly_DoesNotMakePlatformToolsCallable()
    {
        ActAsRun(Dispatcher, RunScopedCredential.UserMcpTools);

        // 只带用户工具授权的凭据调不了平台自己的工具；平台的工具名是保留的，同名的用户工具也不会顶上去
        (await _host.CallToolAsync("delete_everything", null)).IsError.ShouldBe(true);
        (await _host.CallToolAsync("create_ticket", null)).IsError.ShouldBe(true);
        (await _host.ListToolsAsync()).Select(t => t.Name).ShouldBe(["github__search", "github__delete_repo"], ignoreOrder: true);
        _invoked.ShouldBeEmpty();
    }

    [Fact]
    public async Task PlatformToolWithSameName_IsNotShadowedByTheUsersServer()
    {
        ActAsRun(Dispatcher, "create_ticket", RunScopedCredential.UserMcpTools);

        var result = await _host.CallToolAsync("create_ticket", null);

        Text(result).ShouldContain("ticket-1");
        _invoked.ShouldBeEmpty();
    }

    [Fact]
    public async Task RejectedByApproval_IsReportedAsAnError()
    {
        ActAsRun(Dispatcher, RunScopedCredential.UserMcpTools);

        var result = await _host.CallToolAsync("github__delete_repo", null);

        result.IsError.ShouldBe(true);
        Text(result).ShouldContain("Tool call rejected");
    }

    [Fact]
    public async Task UnknownTool_IsNotExposed()
    {
        ActAsRun(Dispatcher, RunScopedCredential.UserMcpTools);

        var result = await _host.CallToolAsync("github__nope", null);

        result.IsError.ShouldBe(true);
        Text(result).ShouldContain("not exposed");
    }

    [Fact]
    public async Task ProviderFailure_ListsNoUserTools_DoesNotBreakPlatformTools()
    {
        _provider.Setup(p => p.GetUserToolsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));
        ActAsRun(Dispatcher, "create_ticket", RunScopedCredential.UserMcpTools);

        (await _host.ListToolsAsync()).Select(t => t.Name).ShouldBe(["create_ticket"]);
    }

    private static Dictionary<string, System.Text.Json.JsonElement> Args(string key, string value)
        => new() { [key] = System.Text.Json.JsonSerializer.SerializeToElement(value) };

    private sealed class HttpContextAccessorStub : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class TenantStub : ICurrentTenant
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
