using System.IO.Pipelines;
using System.Text.RegularExpressions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Tnzi.AI.Infrastructure.Mcp;

namespace Tnzi.AI.Tests.Infrastructure.Mcp;

/// <summary>
/// <c>PrefixToolNameWithServer</c> 对着两个真实的进程内 MCP 服务器跑：两台都提供 <c>search</c>。
/// </summary>
/// <remarks>
/// 此前前缀只写在一条把协议层 Tool 转成 AIFunction 的反射路径里，而 SDK 给出的 McpClientTool 本身就是 AIFunction，
/// 那条路径从不执行：开了前缀名字也不变，第二台服务器的 <c>search</c> 被当成重名静默丢掉。
/// </remarks>
public sealed class McpToolPrefixTests : IAsyncLifetime
{
    private readonly List<InProcessServer> _servers = [];
    private readonly Dictionary<string, McpClientAdapter> _adapters = new(StringComparer.Ordinal);

    public async Task InitializeAsync()
    {
        foreach (var name in new[] { "alpha", "beta" })
        {
            var server = await InProcessServer.StartAsync(name);
            _servers.Add(server);
            _adapters[name] = new McpClientAdapter(server.Client, name, NullLogger.Instance, prefixToolNameWithServer: true);
        }
    }

    public async Task DisposeAsync()
    {
        foreach (var adapter in _adapters.Values)
        {
            await adapter.DisposeAsync();
        }
        foreach (var server in _servers)
        {
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task SameToolOnTwoServers_BothReachTheModelUnderProviderSafeNames_AndCallTheirOwnServer()
    {
        // AllowedTools may name the tool by its server-side name or by the prefixed name
        var alpha = new McpServerConfig { Name = "alpha", PrefixToolNameWithServer = true, ApprovalMode = McpApprovalMode.NeverRequire, AllowedTools = ["search"] };
        var beta = new McpServerConfig { Name = "beta", PrefixToolNameWithServer = true, ApprovalMode = McpApprovalMode.NeverRequire, AllowedTools = ["beta__search"] };
        var provider = CreateProvider(alpha, beta);

        var tools = await provider.GetToolsAsync();

        tools.Select(t => t.Name).ShouldBe(["alpha__search", "beta__search"], ignoreOrder: true);
        tools.ShouldAllBe(t => Regex.IsMatch(t.Name, "^[a-zA-Z0-9_-]{1,64}$"));
        tools.Select(McpToolNames.OriginalName).ShouldAllBe(n => n == "search");

        foreach (var tool in tools.Cast<AIFunction>())
        {
            var result = await tool.InvokeAsync(new AIFunctionArguments { ["query"] = "x" });
            var expectedServer = tool.Name.Split("__")[0];
            JsonSerializer.Serialize(result).ShouldContain($"from {expectedServer}");
        }
    }

    [Fact]
    public async Task AllowedTools_NotMatchingEitherName_FiltersTheToolOut()
    {
        var alpha = new McpServerConfig { Name = "alpha", PrefixToolNameWithServer = true, ApprovalMode = McpApprovalMode.NeverRequire, AllowedTools = ["fetch"] };
        var provider = CreateProvider(alpha);

        (await provider.GetToolsAsync()).ShouldBeEmpty();
    }

    private McpToolProvider CreateProvider(params McpServerConfig[] servers)
    {
        var options = new AIOptions { Mcp = new McpOptions { Enabled = true, ToolCacheSeconds = 300, Servers = servers.ToList() } };
        var factory = new Mock<IMcpClientFactory>();
        factory.Setup(f => f.GetOrCreateClientAsync(It.IsAny<McpServerConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((McpServerConfig config, CancellationToken _) => _adapters[config.Name]);
        return new McpToolProvider(
            new StaticOptionsMonitor<AIOptions>(options),
            new OptionsBackedMcpServerCatalog(Microsoft.Extensions.Options.Options.Create(options)),
            factory.Object,
            NullLoggerFactory.Instance,
            NullLogger<McpToolProvider>.Instance);
    }

    private sealed class InProcessServer : IAsyncDisposable
    {
        private readonly Pipe _clientToServer = new();
        private readonly Pipe _serverToClient = new();
        private readonly CancellationTokenSource _cts = new();
        private McpServer _server = null!;
        private Task _run = Task.CompletedTask;

        public McpClient Client { get; private set; } = null!;

        public static async Task<InProcessServer> StartAsync(string name)
        {
            var host = new InProcessServer();
            var options = new McpServerOptions
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
                    CallToolHandler = (request, _) => ValueTask.FromResult(new CallToolResult
                    {
                        Content = [new TextContentBlock { Text = $"{request.Params!.Name} from {name}" }]
                    })
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
}
