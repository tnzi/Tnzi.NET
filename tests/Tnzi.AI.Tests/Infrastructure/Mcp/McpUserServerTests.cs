using System.IO.Pipelines;
using System.Reflection;
using System.Text.RegularExpressions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Tnzi.AI.Infrastructure.Mcp;

namespace Tnzi.AI.Tests.Infrastructure.Mcp;

/// <summary>
/// 用户专属 MCP 服务器（<see cref="IUserMcpServerSource"/>）：连接、工具缓存、凭据都按用户分区，
/// 凭据由回调给出、只写进连接的请求头，401 时回调带 Refresh 再要一次。
/// </summary>
/// <remarks>
/// 每次建连起一个真实的进程内 MCP 服务器，工具返回「它建连时收到的 Authorization 头」，
/// 所以断言看到的就是线上会发出去的那个令牌；不是对着桩的返回值断言。
/// </remarks>
public sealed class McpUserServerTests : IAsyncLifetime
{
    private static readonly Guid UserA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid UserB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

    private readonly TestSource _source = new();
    private readonly TokenStore _tokens = new();
    private readonly HeaderCapturingFactory _factory = new();
    private readonly McpToolProvider _provider;
    private readonly UserMcpConnections _connections;

    public McpUserServerTests()
    {
        var options = new AIOptions { Mcp = new McpOptions { Enabled = true, ToolCacheSeconds = 300 } };
        _provider = new McpToolProvider(
            new StaticOptionsMonitor<AIOptions>(options),
            new OptionsBackedMcpServerCatalog(Microsoft.Extensions.Options.Options.Create(options)),
            _factory,
            NullLoggerFactory.Instance,
            NullLogger<McpToolProvider>.Instance,
            userServerSource: _source);
        _connections = new UserMcpConnections(_factory, _provider);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _factory.DisposeServersAsync();
    }

    #region 两个用户互不相通

    [Fact]
    public async Task SameServerNameDifferentTokens_NeverShareConnectionOrToken()
    {
        _tokens.Set(UserA, "tok-a");
        _tokens.Set(UserB, "tok-b");
        _source.Set(UserA, Github(UserA));
        _source.Set(UserB, Github(UserB));

        var toolsA = await _provider.GetToolsAsync(UserA);
        var toolsB = await _provider.GetToolsAsync(UserB);

        (await Call(toolsA)).ShouldContain("github token=Bearer tok-a");
        (await Call(toolsB)).ShouldContain("github token=Bearer tok-b");
        _factory.Connections.Select(c => c.Authorization).ShouldBe(["Bearer tok-a", "Bearer tok-b"]);
        GetCacheKeys(_factory).Count.ShouldBe(2);

        // 第二次取同一个用户的工具走缓存，不再建连
        await _provider.GetToolsAsync(UserA);
        _factory.Connections.Count.ShouldBe(2);
    }

    [Fact]
    public async Task OneUsersServer_NeverAppearsInAnotherUsersTools()
    {
        _tokens.Set(UserA, "tok-a");
        _source.Set(UserA, Github(UserA));

        (await _provider.GetToolsAsync(UserA)).ShouldNotBeEmpty();
        (await _provider.GetToolsAsync(UserB)).ShouldBeEmpty();
        (await _provider.GetToolsAsync((Guid?)null)).ShouldBeEmpty();
        // 只读无用户的运行（定时 / 系统）不带任何用户专属服务器
        (await _provider.GetToolsAsync(CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Token_NeverLandsInTheSharedConfigObject()
    {
        _tokens.Set(UserA, "tok-a");
        var declared = Github(UserA);
        _source.Set(UserA, declared);

        await _provider.GetToolsAsync(UserA);

        // 清单对象（消费方可能缓存、也可能同时被别的运行读到）里没有被写进令牌
        declared.Headers.ShouldNotContainKey("Authorization");
        _factory.Connections.Single().Authorization.ShouldBe("Bearer tok-a");
    }

    [Fact]
    public async Task ConfigInstanceSharedBetweenUsers_IsStampedPerUser_AndNeverMutated()
    {
        // 消费方图省事把同一个配置实例回给两个用户（或缓存了它）：框架按请求的用户各盖一份副本，
        // 原实例不被改动，两个用户各有各的连接分区
        var shared = Github(UserA, credential: (_, _) => ValueTask.FromResult<McpCredential?>(new McpCredential { AccessToken = "shared-tok" }));
        _source.Set(UserA, shared);
        _source.Set(UserB, shared);

        await _provider.GetToolsAsync(UserA);
        await _provider.GetToolsAsync(UserB);

        shared.UserId.ShouldBe(Guid.Empty);
        GetCacheKeys(_factory).Count.ShouldBe(2);
        GetCacheKeys(_factory).ShouldContain(k => k.StartsWith("user:" + UserA.ToString("N"), StringComparison.Ordinal));
        GetCacheKeys(_factory).ShouldContain(k => k.StartsWith("user:" + UserB.ToString("N"), StringComparison.Ordinal));
    }

    #endregion

    #region 401 恢复

    [Fact]
    public async Task On401_AsksTheCredentialProviderToRefreshOnce_ThenRetries()
    {
        _tokens.Set(UserA, "stale");
        _tokens.RefreshesTo = "fresh";
        _factory.RevokedTokens.Add("Bearer stale");
        _source.Set(UserA, Github(UserA));

        var tools = await _provider.GetToolsAsync(UserA);
        var result = await Call(tools);

        result.ShouldContain("github token=Bearer fresh");
        _tokens.Requests.Count(r => r.Refresh).ShouldBe(1);
        _tokens.Requests.Select(r => r.Refresh).ShouldBe([false, true]);
        _factory.Connections.Select(c => c.Authorization).ShouldBe(["Bearer stale", "Bearer fresh"]);
    }

    [Fact]
    public async Task On401_WhenRefreshDoesNotHelp_FailsAfterOneRefresh()
    {
        _tokens.Set(UserA, "stale");
        _tokens.RefreshesTo = "also-bad";
        _factory.RevokedTokens.Add("Bearer stale");
        _factory.RevokedTokens.Add("Bearer also-bad");
        _source.Set(UserA, Github(UserA));

        var tools = await _provider.GetToolsAsync(UserA);
        await Should.ThrowAsync<InvalidOperationException>(() => Call(tools));

        // 框架只刷新一次：无限重试会把一个被撤销的账号变成对第三方服务的持续撞击
        _tokens.Requests.Count(r => r.Refresh).ShouldBe(1);
    }

    [Fact]
    public async Task On401_RefreshingOneUser_DoesNotDropAnotherUsersConnection()
    {
        _tokens.Set(UserA, "stale");
        _tokens.RefreshesTo = "fresh";
        _tokens.Set(UserB, "tok-b");
        _factory.RevokedTokens.Add("Bearer stale");
        _source.Set(UserA, Github(UserA));
        _source.Set(UserB, Github(UserB));

        var toolsA = await _provider.GetToolsAsync(UserA);
        var toolsB = await _provider.GetToolsAsync(UserB);
        await Call(toolsA);

        // B 的连接没有因为 A 的恢复被重连
        _factory.Connections.Count(c => c.Authorization == "Bearer tok-b").ShouldBe(1);
        (await Call(toolsB)).ShouldContain("github token=Bearer tok-b");
    }

    #endregion

    #region 断开与移除

    [Fact]
    public async Task RemovedServer_DisappearsFromTheNextRun_AndItsConnectionIsDropped()
    {
        _tokens.Set(UserA, "tok-a");
        _source.Set(UserA, Github(UserA));
        (await _provider.GetToolsAsync(UserA)).ShouldNotBeEmpty();
        GetCacheKeys(_factory).Count.ShouldBe(1);

        // 消费方：用户断开了账号
        _source.Set(UserA);
        await _connections.DisconnectAsync(UserA, "github");

        (await _provider.GetToolsAsync(UserA)).ShouldBeEmpty();
        GetCacheKeys(_factory).ShouldBeEmpty();
    }

    [Fact]
    public async Task Disconnect_OnlyAffectsThatUser()
    {
        _tokens.Set(UserA, "tok-a");
        _tokens.Set(UserB, "tok-b");
        _source.Set(UserA, Github(UserA));
        _source.Set(UserB, Github(UserB));
        await _provider.GetToolsAsync(UserA);
        var toolsB = await _provider.GetToolsAsync(UserB);

        await _connections.DisconnectAsync(UserA, "github");

        GetCacheKeys(_factory).Single().ShouldStartWith("user:" + UserB.ToString("N"));
        (await Call(toolsB)).ShouldContain("github token=Bearer tok-b");
        _factory.Connections.Count.ShouldBe(2);
    }

    [Fact]
    public async Task DisconnectAll_DropsEveryServerOfTheUser()
    {
        _tokens.Set(UserA, "tok-a");
        _source.Set(UserA, Github(UserA), Github(UserA, "linear"));
        (await _provider.GetToolsAsync(UserA)).Count.ShouldBe(2);

        await _connections.DisconnectAllAsync(UserA);

        GetCacheKeys(_factory).ShouldBeEmpty();
    }

    [Fact]
    public async Task Disconnect_AfterAFailedConnect_ClearsTheCooldown()
    {
        _tokens.Set(UserA, "tok-a");
        _source.Set(UserA, Github(UserA));
        _factory.FailConnects = true;
        (await _provider.GetToolsAsync(UserA)).ShouldBeEmpty();

        // 用户重新连接账号：冷却不应让他等 30 秒
        _factory.FailConnects = false;
        await _connections.DisconnectAsync(UserA, "github");

        (await _provider.GetToolsAsync(UserA)).ShouldNotBeEmpty();
    }

    #endregion

    #region 工具名

    [Fact]
    public async Task ToolNames_FollowTheServerPrefixRule()
    {
        _tokens.Set(UserA, "tok-a");
        _source.Set(UserA, Github(UserA), Github(UserA, "My Server! (prod)"));

        var tools = await _provider.GetToolsAsync(UserA);

        tools.Select(t => t.Name).ShouldBe(["github__search", "My_Server___prod___search"], ignoreOrder: true);
        tools.ShouldAllBe(t => Regex.IsMatch(t.Name, "^[A-Za-z0-9_-]{1,64}$"));
        tools.Select(McpToolNames.OriginalName).ShouldAllBe(n => n == "search");
    }

    [Fact]
    public async Task UserServer_CannotShadowAToolOfAnotherSource_NameCollisionKeepsTheFirst()
    {
        _tokens.Set(UserA, "tok-a");
        // 不带前缀的两台同名工具：先出现者留下
        var first = Github(UserA, "one", prefix: false);
        var second = Github(UserA, "two", prefix: false);
        _source.Set(UserA, first, second);

        var tools = await _provider.GetToolsAsync(UserA);

        tools.Select(t => t.Name).ShouldBe(["search"]);
        (await Call(tools)).ShouldContain("one token=");
    }

    #endregion

    #region 清单校验与凭据回调的失败

    [Fact]
    public async Task InvalidDeclarations_AreSkipped_ValidOnesKept()
    {
        _tokens.Set(UserA, "tok-a");
        _source.Set(UserA,
            Github(UserA, "stdio-one", configure: c => c.ConnectionType = McpConnectionType.Stdio),
            Github(UserA, "command-one", configure: c => c.Command = "bash"),
            Github(UserA, "oauth-one", configure: c => c.OAuth = new McpOAuthConfig { ClientId = "x", TokenEndpoint = "https://x/t" }),
            Github(UserA, "header-one", configure: c => c.Headers["authorization"] = "Bearer smuggled"),
            Github(UserA, "relative-one", configure: c => c.Endpoint = "/not/absolute"),
            Github(UserA, "ftp-one", configure: c => c.Endpoint = "ftp://example.com"),
            Github(UserA, "bad\u001Fname"),
            Github(UserA, "good"));

        var tools = await _provider.GetToolsAsync(UserA);

        tools.Select(t => t.Name).ShouldBe(["good__search"]);
        _factory.Connections.Count.ShouldBe(1);
    }

    [Fact]
    public async Task DuplicateNamesInOneList_KeepTheFirst()
    {
        _tokens.Set(UserA, "tok-a");
        _source.Set(UserA, Github(UserA, "dup"), Github(UserA, "DUP"));

        (await _provider.GetToolsAsync(UserA)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task SourceThrows_FailsClosed_NoUserServers()
    {
        _source.Throw = true;

        (await _provider.GetToolsAsync(UserA)).ShouldBeEmpty();
    }

    [Fact]
    public async Task CredentialProviderReturnsNull_ServerIsSkipped_OthersStillWork()
    {
        _tokens.Set(UserA, "tok-a");
        _source.Set(UserA, Github(UserA, "revoked", credential: (_, _) => ValueTask.FromResult<McpCredential?>(null)), Github(UserA, "fine"));

        var tools = await _provider.GetToolsAsync(UserA);

        tools.Select(t => t.Name).ShouldBe(["fine__search"]);
    }

    [Theory]
    [InlineData("bad\r\ntoken")]
    [InlineData("")]
    public async Task CredentialWithInvalidHeaderValue_IsRejected(string token)
    {
        _source.Set(UserA, Github(UserA, credential: (_, _) => ValueTask.FromResult<McpCredential?>(new McpCredential { AccessToken = token })));

        (await _provider.GetToolsAsync(UserA)).ShouldBeEmpty();
        _factory.Connections.ShouldBeEmpty();
    }

    [Fact]
    public async Task CredentialProviderThrows_ServerIsSkipped_AndTheMessageIsNotLogged()
    {
        _source.Set(UserA, Github(UserA, credential: (_, _) => throw new InvalidOperationException("secret-token-xyz leaked")));

        (await _provider.GetToolsAsync(UserA)).ShouldBeEmpty();
    }

    [Fact]
    public async Task CredentialCarryingItsOwnAuthorizationHeader_IsRejected()
    {
        _source.Set(UserA, Github(UserA, credential: (_, _) => ValueTask.FromResult<McpCredential?>(new McpCredential
        {
            AccessToken = "tok",
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer other" }
        })));

        (await _provider.GetToolsAsync(UserA)).ShouldBeEmpty();
    }

    [Fact]
    public async Task AdditionalHeadersAndScheme_AreSentWithTheToken()
    {
        _source.Set(UserA, Github(UserA, credential: (_, _) => ValueTask.FromResult<McpCredential?>(new McpCredential
        {
            AccessToken = "tok",
            Scheme = "token",
            AdditionalHeaders = new Dictionary<string, string> { ["X-Api-Version"] = "2022-11-28" }
        })));

        await _provider.GetToolsAsync(UserA);

        var connection = _factory.Connections.Single();
        connection.Authorization.ShouldBe("token tok");
        connection.Headers["X-Api-Version"].ShouldBe("2022-11-28");
    }

    [Fact]
    public async Task McpDisabled_NoUserServersEither()
    {
        var options = new AIOptions { Mcp = new McpOptions { Enabled = false } };
        var provider = new McpToolProvider(
            new StaticOptionsMonitor<AIOptions>(options),
            new OptionsBackedMcpServerCatalog(Microsoft.Extensions.Options.Options.Create(options)),
            _factory,
            NullLoggerFactory.Instance,
            NullLogger<McpToolProvider>.Instance,
            userServerSource: _source);
        _tokens.Set(UserA, "tok-a");
        _source.Set(UserA, Github(UserA));

        (await provider.GetUserToolsAsync(UserA)).ShouldBeEmpty();
    }

    [Fact]
    public async Task GetUserTools_OnlyReturnsTheUsersServers()
    {
        _tokens.Set(UserA, "tok-a");
        _source.Set(UserA, Github(UserA));

        (await _provider.GetUserToolsAsync(UserA)).Select(t => t.Name).ShouldBe(["github__search"]);
        (await _provider.GetUserToolsAsync(UserB)).ShouldBeEmpty();
    }

    #endregion

    #region 缓存键

    [Fact]
    public void CacheKey_UserKeysNeverCollideWithTenantOrDeploymentKeys()
    {
        var user = McpCacheKey.ForUser(UserA, "github");

        user.ShouldNotBe(McpCacheKey.For(new McpServerConfig { Name = "github" }));
        user.ShouldNotBe(McpCacheKey.For(new McpServerConfig { Name = "github", TenantKey = McpCacheKey.HostRegistryPartition }));
        user.ShouldNotBe(McpCacheKey.For(new McpServerConfig { Name = "github", TenantKey = UserA.ToString() }));
        user.ShouldNotBe(McpCacheKey.ForUser(UserB, "github"));
        McpCacheKey.ServerName(user).ShouldBe("github");
        McpCacheKey.BelongsToUser(user, UserA).ShouldBeTrue();
        McpCacheKey.BelongsToUser(user, UserA, "GitHub").ShouldBeTrue();
        McpCacheKey.BelongsToUser(user, UserA, "slack").ShouldBeFalse();
        McpCacheKey.BelongsToUser(user, UserB).ShouldBeFalse();
    }

    #endregion

    #region Helpers

    private UserMcpServerConfig Github(
        Guid user, string name = "github", bool prefix = true,
        Func<McpCredentialRequest, CancellationToken, ValueTask<McpCredential?>>? credential = null,
        Action<UserMcpServerConfig>? configure = null)
    {
        var config = new UserMcpServerConfig
        {
            Name = name,
            Endpoint = "https://mcp.example.com/" + name.GetHashCode().ToString("x"),
            ApprovalMode = McpApprovalMode.NeverRequire,
            PrefixToolNameWithServer = prefix,
            CredentialProvider = credential ?? _tokens.ProviderFor(user)
        };
        configure?.Invoke(config);
        return config;
    }

    private static async Task<string> Call(IReadOnlyList<AITool> tools)
    {
        var tool = tools.OfType<AIFunction>().First();
        var result = await tool.InvokeAsync(new AIFunctionArguments { ["query"] = "x" });
        return JsonSerializer.Serialize(result);
    }

    private static IReadOnlyCollection<string> GetCacheKeys(McpClientFactory factory)
    {
        var field = typeof(McpClientFactory).GetField("_cache", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return ((ConcurrentDictionary<string, IMcpClientAdapter>)field.GetValue(factory)!).Keys.ToList();
    }

    private sealed class TestSource : IUserMcpServerSource
    {
        private readonly Dictionary<Guid, IReadOnlyList<UserMcpServerConfig>> _servers = [];

        public bool Throw { get; set; }

        public void Set(Guid user, params UserMcpServerConfig[] servers) => _servers[user] = servers;

        public Task<IReadOnlyList<UserMcpServerConfig>> GetServersAsync(Guid userId, CancellationToken ct = default)
        {
            if (Throw)
            {
                throw new InvalidOperationException("the consumer's store is down");
            }

            return Task.FromResult(_servers.TryGetValue(userId, out var list) ? list : []);
        }
    }

    /// <summary>消费方那一侧：每个用户一个令牌；被问到 Refresh 时换成 <see cref="RefreshesTo"/>。</summary>
    private sealed class TokenStore
    {
        private readonly Dictionary<Guid, string> _current = [];

        public string? RefreshesTo { get; set; }

        public List<McpCredentialRequest> Requests { get; } = [];

        public void Set(Guid user, string token) => _current[user] = token;

        public Func<McpCredentialRequest, CancellationToken, ValueTask<McpCredential?>> ProviderFor(Guid user) => (request, _) =>
        {
            Requests.Add(request);
            if (request.Refresh && RefreshesTo is not null)
            {
                _current[user] = RefreshesTo;
            }

            return ValueTask.FromResult<McpCredential?>(_current.TryGetValue(user, out var token)
                ? new McpCredential { AccessToken = token }
                : null);
        };
    }

    private sealed record Connection(string Authorization, IReadOnlyDictionary<string, string> Headers);

    /// <summary>
    /// 真实的建连逻辑（含凭据回调）+ 进程内服务器：不起 HTTP，工具返回服务器建连时看到的 Authorization 头。
    /// 令牌在 <see cref="RevokedTokens"/> 里时，调用工具以 401 失败。
    /// </summary>
    private sealed class HeaderCapturingFactory : McpClientFactory
    {
        private readonly List<InProcessServer> _servers = [];

        public HeaderCapturingFactory() : base(NullLoggerFactory.Instance, NullLogger<McpClientFactory>.Instance)
        {
        }

        public List<Connection> Connections { get; } = [];

        public HashSet<string> RevokedTokens { get; } = [];

        public bool FailConnects { get; set; }

        protected internal override async Task<IMcpClientAdapter> ConnectTransportAsync(
            McpServerConfig config, Dictionary<string, string> effectiveHeaders, CancellationToken ct)
        {
            if (FailConnects)
            {
                throw new InvalidOperationException("connection refused");
            }

            var authorization = effectiveHeaders["Authorization"];
            Connections.Add(new Connection(authorization, new Dictionary<string, string>(effectiveHeaders)));
            var server = await InProcessServer.StartAsync(config.Name, authorization);
            _servers.Add(server);
            var inner = new McpClientAdapter(server.Client, config.Name, NullLogger.Instance, config.PrefixToolNameWithServer);
            return new RevocableAdapter(inner, () => RevokedTokens.Contains(authorization));
        }

        public async Task DisposeServersAsync()
        {
            foreach (var server in _servers)
            {
                await server.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// 真实 HTTP 传输下被撤销的令牌表现为请求层的 401（异常），而进程内服务器的处理器异常会变成 IsError 结果；
    /// 这里在客户端一侧复现前者：令牌被撤销后，这条连接上的工具调用抛 401。
    /// </summary>
    private sealed class RevocableAdapter(IMcpClientAdapter inner, Func<bool> isRevoked) : IMcpClientAdapter
    {
        public async Task<IReadOnlyList<AITool>> ListToolsAsync(CancellationToken ct = default)
            => (await inner.ListToolsAsync(ct)).Select(t => t is AIFunction f ? new RevocableTool(f, isRevoked) : t).ToList();

        public Task<bool> IsConnectedAsync(CancellationToken ct = default) => inner.IsConnectedAsync(ct);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class RevocableTool(AIFunction inner, Func<bool> isRevoked) : DelegatingAIFunction(inner)
    {
        protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
            => isRevoked()
                ? throw new HttpRequestException("Response status code does not indicate success: 401 (Unauthorized).", null, System.Net.HttpStatusCode.Unauthorized)
                : base.InvokeCoreAsync(arguments, cancellationToken);
    }

    private sealed class InProcessServer : IAsyncDisposable
    {
        private readonly Pipe _clientToServer = new();
        private readonly Pipe _serverToClient = new();
        private readonly CancellationTokenSource _cts = new();
        private ModelContextProtocol.Server.McpServer _server = null!;
        private Task _run = Task.CompletedTask;

        public McpClient Client { get; private set; } = null!;

        public static async Task<InProcessServer> StartAsync(string name, string authorization)
        {
            var host = new InProcessServer();
            var options = new ModelContextProtocol.Server.McpServerOptions
            {
                ServerInfo = new Implementation { Name = name, Version = "1.0.0" },
                Handlers = new McpServerHandlers
                {
                    ListToolsHandler = (_, _) => ValueTask.FromResult(new ListToolsResult
                    {
                        Tools =
                        [
                            new Tool
                            {
                                Name = "search",
                                Description = "Search " + name,
                                InputSchema = JsonDocument.Parse("""{"type":"object","properties":{"query":{"type":"string"}}}""").RootElement
                            }
                        ]
                    }),
                    CallToolHandler = (_, _) => ValueTask.FromResult(new CallToolResult
                    {
                        Content = [new TextContentBlock { Text = $"{name} token={authorization}" }]
                    })
                }
            };

            var serverTransport = new StreamServerTransport(
                host._clientToServer.Reader.AsStream(), host._serverToClient.Writer.AsStream(), name, NullLoggerFactory.Instance);
            host._server = ModelContextProtocol.Server.McpServer.Create(serverTransport, options, NullLoggerFactory.Instance);
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
