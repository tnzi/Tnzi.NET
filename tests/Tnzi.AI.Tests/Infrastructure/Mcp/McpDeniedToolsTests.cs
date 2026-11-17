using System.IO.Pipelines;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Tnzi.AI.Infrastructure.Mcp;

namespace Tnzi.AI.Tests.Infrastructure.Mcp;

/// <summary>
/// <see cref="McpServerConfig.DeniedTools"/>：管理员禁掉一台服务器上的个别工具，其余工具照常提供。
/// </summary>
/// <remarks>
/// 对着真实的进程内 MCP 服务器跑（三个工具：search / delete_repo / send_email），服务器记下每一次收到的
/// <c>tools/call</c>：「被拒绝」的断言看的是服务器有没有收到调用，而不是包装层的返回值。
/// </remarks>
public sealed class McpDeniedToolsTests : IAsyncLifetime
{
    private static readonly Guid UserA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");

    private InProcessServer _server = null!;
    private McpClientAdapter _prefixedAdapter = null!;
    private McpClientAdapter _plainAdapter = null!;

    public async Task InitializeAsync()
    {
        _server = await InProcessServer.StartAsync("github");
        _prefixedAdapter = new McpClientAdapter(_server.Client, "github", NullLogger.Instance, prefixToolNameWithServer: true);
        _plainAdapter = new McpClientAdapter(_server.Client, "github", NullLogger.Instance, prefixToolNameWithServer: false);
    }

    public async Task DisposeAsync()
    {
        await _prefixedAdapter.DisposeAsync();
        await _plainAdapter.DisposeAsync();
        await _server.DisposeAsync();
    }

    #region 清单过滤

    [Fact]
    public async Task DeniedTool_IsNotOffered_TheServersOtherToolsStay()
    {
        var provider = CreateProvider(Server(denied: ["delete_repo"]));

        var tools = await provider.GetToolsAsync();

        tools.Select(t => t.Name).ShouldBe(["github__search", "github__send_email"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("delete_repo")]
    [InlineData("github__delete_repo")]
    [InlineData("DELETE_REPO")]
    [InlineData("GitHub__Delete_Repo")]
    public async Task DeniedTools_AcceptTheServerNameOrThePrefixedName_IgnoringCase(string entry)
    {
        var provider = CreateProvider(Server(denied: [entry]));

        (await provider.GetToolsAsync()).Select(t => t.Name).ShouldNotContain("github__delete_repo");
    }

    [Fact]
    public async Task DeniedTools_WithoutPrefix_FilterByTheServerName()
    {
        var provider = CreateProvider(Server(prefix: false, denied: ["send_email"]));

        (await provider.GetToolsAsync()).Select(t => t.Name).ShouldBe(["search", "delete_repo"], ignoreOrder: true);
    }

    [Fact]
    public async Task DenyWinsOverTheAllowList()
    {
        var provider = CreateProvider(Server(allowed: ["search", "delete_repo"], denied: ["delete_repo"]));

        (await provider.GetToolsAsync()).Select(t => t.Name).ShouldBe(["github__search"]);
    }

    [Fact]
    public async Task BlankEntries_DenyNothing()
    {
        var provider = CreateProvider(Server(denied: ["", "  "]));

        (await provider.GetToolsAsync()).Count.ShouldBe(3);
    }

    [Fact]
    public async Task DenyingAToolLater_TakesEffectOnTheNextListing_EvenWithAWarmToolCache()
    {
        var server = Server();
        var provider = CreateProvider(server);
        (await provider.GetToolsAsync()).Count.ShouldBe(3);

        // 工具缓存存的是未过滤的清单：改名单不需要失效缓存，下一次取工具就按新名单过滤
        server.DeniedTools = ["delete_repo"];

        (await provider.GetToolsAsync()).Select(t => t.Name).ShouldNotContain("github__delete_repo");
    }

    [Fact]
    public async Task OnlyTheConfiguredServerIsAffected()
    {
        await using var other = await InProcessServer.StartAsync("gitlab");
        await using var otherAdapter = new McpClientAdapter(other.Client, "gitlab", NullLogger.Instance, prefixToolNameWithServer: true);
        var provider = CreateProvider(
            new Dictionary<string, IMcpClientAdapter> { ["github"] = _prefixedAdapter, ["gitlab"] = otherAdapter },
            [Server(denied: ["delete_repo"]), new McpServerConfig { Name = "gitlab", PrefixToolNameWithServer = true, ApprovalMode = McpApprovalMode.NeverRequire }]);

        var names = (await provider.GetToolsAsync()).Select(t => t.Name).ToList();

        names.ShouldNotContain("github__delete_repo");
        names.ShouldContain("gitlab__delete_repo");
    }

    #endregion

    #region 与审批名单的组合

    [Fact]
    public async Task DeniedTool_IsNeitherResurrectedByNeverRequireApproval_NorSentToAnApprover()
    {
        var approver = new RecordingApprover();
        var provider = CreateProvider(
            Server(
                approvalMode: McpApprovalMode.Specific,
                denied: ["delete_repo"],
                configure: s =>
                {
                    s.NeverRequireApprovalTools = ["search", "delete_repo"];
                    s.AlwaysRequireApprovalTools = ["send_email"];
                }),
            approver);

        var tools = (await provider.GetToolsAsync()).Cast<AIFunction>().ToDictionary(t => t.Name);

        tools.Keys.ShouldBe(["github__search", "github__send_email"], ignoreOrder: true);

        // 其余工具的审批语义不受禁用名单影响
        await tools["github__send_email"].InvokeAsync(new AIFunctionArguments());
        await tools["github__search"].InvokeAsync(new AIFunctionArguments());
        approver.Requests.ShouldBe(["github__send_email"]);
        _server.Calls.ShouldBe(["send_email", "search"]);
    }

    #endregion

    #region 用户专属服务器

    [Fact]
    public async Task UserServer_DenyingOneToolKeepsTheRestOfTheAccount()
    {
        var source = new SingleUserSource(UserA, new UserMcpServerConfig
        {
            Name = "github",
            Endpoint = "https://mcp.example.com/github",
            ApprovalMode = McpApprovalMode.NeverRequire,
            DeniedTools = ["send_email"],
            CredentialProvider = (_, _) => ValueTask.FromResult<McpCredential?>(new McpCredential { AccessToken = "tok" })
        });
        var provider = CreateProvider(new Dictionary<string, IMcpClientAdapter> { ["github"] = _prefixedAdapter }, [], userSource: source);

        var userTools = await provider.GetUserToolsAsync(UserA);
        var runTools = await provider.GetToolsAsync(UserA);

        userTools.Select(t => t.Name).ShouldBe(["github__search", "github__delete_repo"], ignoreOrder: true);
        runTools.Select(t => t.Name).ShouldBe(["github__search", "github__delete_repo"], ignoreOrder: true);
    }

    #endregion

    #region 调用时的第二道检查

    [Fact]
    public async Task DeniedToolThatBypassedTheListFilter_IsRefusedWithoutReachingTheServer()
    {
        // 一个没经过清单过滤的工具对象（模拟任何绕开过滤的路径）：调用在发往服务器之前被拒绝
        var raw = (await _prefixedAdapter.ListToolsAsync()).OfType<AIFunction>().Single(t => t.Name == "github__delete_repo");
        var denied = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "delete_repo" };
        var guarded = McpDeniedToolGuard.Wrap([raw], "github", denied).Single().ShouldBeOfType<McpDeniedToolGuard>();

        var result = await guarded.InvokeAsync(new AIFunctionArguments { ["owner"] = "acme" });

        result.ShouldBeOfType<string>().ShouldStartWith(McpDeniedToolGuard.RejectionPrefix);
        ((string)result!).ShouldContain("github__delete_repo");
        ((string)result).ShouldContain("denied");
        _server.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Guard_RecognisesTheServerNameThroughOtherWrappers()
    {
        var raw = (await _prefixedAdapter.ListToolsAsync()).OfType<AIFunction>().Single(t => t.Name == "github__delete_repo");
        var wrapped = new McpAuditToolWrapper(raw, "github");
        var denied = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DELETE_REPO" };

        var result = await new McpDeniedToolGuard(wrapped, "github", denied).InvokeAsync(new AIFunctionArguments());

        result.ShouldBeOfType<string>().ShouldStartWith(McpDeniedToolGuard.RejectionPrefix);
        _server.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Guard_LetsToolsOutsideTheDenyListThrough()
    {
        var provider = CreateProvider(Server(denied: ["delete_repo"]));
        var search = (await provider.GetToolsAsync()).OfType<AIFunction>().Single(t => t.Name == "github__search");

        search.GetService<McpDeniedToolGuard>().ShouldNotBeNull();
        JsonSerializer.Serialize(await search.InvokeAsync(new AIFunctionArguments())).ShouldContain("search done");
        _server.Calls.ShouldBe(["search"]);
    }

    [Fact]
    public void Guard_WithAnEmptyDenyList_DoesNotWrap()
    {
        var tool = AIFunctionFactory.Create(() => "ok", "anything");

        McpDeniedToolGuard.Wrap([tool], "github", new HashSet<string>()).Single().ShouldBeSameAs(tool);
    }

    [Fact]
    public void RejectionPrefix_MatchesTheApprovalRejectionPrefix()
    {
        // MCP 回写通道按这个前缀把文本结果标成错误：禁用拒绝与审批拒绝必须同形
        McpDeniedToolGuard.RejectionPrefix.ShouldBe("Tool call rejected:");
    }

    #endregion

    #region Helpers

    private static McpServerConfig Server(
        bool prefix = true,
        List<string>? allowed = null,
        List<string>? denied = null,
        McpApprovalMode approvalMode = McpApprovalMode.NeverRequire,
        Action<McpServerConfig>? configure = null)
    {
        var server = new McpServerConfig
        {
            Name = "github",
            PrefixToolNameWithServer = prefix,
            ApprovalMode = approvalMode,
            AllowedTools = allowed ?? [],
            DeniedTools = denied ?? []
        };
        configure?.Invoke(server);
        return server;
    }

    private McpToolProvider CreateProvider(McpServerConfig server, IToolApprovalHandler? approver = null)
        => CreateProvider(
            new Dictionary<string, IMcpClientAdapter> { ["github"] = server.PrefixToolNameWithServer ? _prefixedAdapter : _plainAdapter },
            [server],
            approver);

    private static McpToolProvider CreateProvider(
        Dictionary<string, IMcpClientAdapter> adapters,
        IEnumerable<McpServerConfig> servers,
        IToolApprovalHandler? approver = null,
        IUserMcpServerSource? userSource = null)
    {
        var options = new AIOptions { Mcp = new McpOptions { Enabled = true, ToolCacheSeconds = 300, Servers = servers.ToList() } };
        var factory = new Mock<IMcpClientFactory>();
        factory.Setup(f => f.GetOrCreateClientAsync(It.IsAny<McpServerConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((McpServerConfig config, CancellationToken _) => adapters[config.Name]);
        return new McpToolProvider(
            new StaticOptionsMonitor<AIOptions>(options),
            new OptionsBackedMcpServerCatalog(Microsoft.Extensions.Options.Options.Create(options)),
            factory.Object,
            NullLoggerFactory.Instance,
            NullLogger<McpToolProvider>.Instance,
            approvalHandler: approver,
            userServerSource: userSource);
    }

    private sealed class RecordingApprover : IToolApprovalHandler
    {
        public List<string> Requests { get; } = [];

        public Task<ToolApprovalResult> RequestApprovalAsync(ToolApprovalRequest request, CancellationToken ct = default)
        {
            Requests.Add(request.ToolName);
            return Task.FromResult(ToolApprovalResult.Approve("tester"));
        }
    }

    private sealed class SingleUserSource(Guid userId, UserMcpServerConfig server) : IUserMcpServerSource
    {
        public Task<IReadOnlyList<UserMcpServerConfig>> GetServersAsync(Guid user, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<UserMcpServerConfig>>(user == userId ? [server] : []);
    }

    /// <summary>进程内 MCP 服务器：三个工具，记下每一次收到的 <c>tools/call</c>。</summary>
    private sealed class InProcessServer : IAsyncDisposable
    {
        private readonly Pipe _clientToServer = new();
        private readonly Pipe _serverToClient = new();
        private readonly CancellationTokenSource _cts = new();
        private McpServer _server = null!;
        private Task _run = Task.CompletedTask;

        public McpClient Client { get; private set; } = null!;

        public ConcurrentQueue<string> Calls { get; } = new();

        public static async Task<InProcessServer> StartAsync(string name)
        {
            var host = new InProcessServer();
            var schema = JsonDocument.Parse("""{"type":"object","properties":{"owner":{"type":"string"}}}""").RootElement;
            var options = new McpServerOptions
            {
                ServerInfo = new Implementation { Name = name, Version = "1.0.0" },
                Handlers = new McpServerHandlers
                {
                    ListToolsHandler = (_, _) => ValueTask.FromResult(new ListToolsResult
                    {
                        Tools =
                        [
                            new Tool { Name = "search", Description = "Search", InputSchema = schema },
                            new Tool { Name = "delete_repo", Description = "Delete a repository", InputSchema = schema },
                            new Tool { Name = "send_email", Description = "Send an email", InputSchema = schema }
                        ]
                    }),
                    CallToolHandler = (request, _) =>
                    {
                        host.Calls.Enqueue(request.Params!.Name);
                        return ValueTask.FromResult(new CallToolResult
                        {
                            Content = [new TextContentBlock { Text = $"{request.Params.Name} done" }]
                        });
                    }
                }
            };

            var serverTransport = new StreamServerTransport(
                host._clientToServer.Reader.AsStream(), host._serverToClient.Writer.AsStream(), name, NullLoggerFactory.Instance);
            host._server = McpServer.Create(serverTransport, options, NullLoggerFactory.Instance);
            host._run = host._server.RunAsync(host._cts.Token);

            var clientTransport = new StreamClientTransport(
                host._clientToServer.Writer.AsStream(), host._serverToClient.Reader.AsStream(), NullLoggerFactory.Instance);
            host.Client = await McpClient.CreateAsync(clientTransport);
            return host;
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            await _server.DisposeAsync();
            try
            {
                await _run;
            }
            catch (OperationCanceledException)
            {
                // 随测试结束被取消
            }

            _cts.Dispose();
        }
    }

    #endregion
}
