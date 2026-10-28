using System.IO.Pipelines;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Tnzi.AI.Infrastructure.Mcp;
using McpPrompt = ModelContextProtocol.Protocol.Prompt;

namespace Tnzi.AI.Tests.Infrastructure.Mcp;

/// <summary>
/// <see cref="McpClientAdapter"/> 的资源 / 提示四个方法对着一个真实的进程内 MCP 服务器跑。
/// </summary>
/// <remarks>
/// SDK 的这四个方法返回 <c>ValueTask&lt;T&gt;</c>；此前的反射层只认 <c>Task</c>，把未等待的 ValueTask 当结果对象解析，
/// 对任何服务器都安静地返回空列表 / null。提供器层的测试全用 <c>Mock&lt;IMcpClientAdapter&gt;</c>，碰不到这一层。
/// </remarks>
public sealed class McpClientAdapterResourcesPromptsTests : IAsyncLifetime
{
    private readonly Pipe _clientToServer = new();
    private readonly Pipe _serverToClient = new();
    private readonly CancellationTokenSource _serverCts = new();
    private McpServer? _server;
    private Task? _serverTask;
    private McpClientAdapter? _adapter;

    public string? ReceivedPromptTopic { get; private set; }

    public async Task InitializeAsync()
    {
        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "docs-server", Version = "1.0.0" },
            Handlers = new McpServerHandlers
            {
                ListResourcesHandler = (_, _) => ValueTask.FromResult(new ListResourcesResult
                {
                    Resources =
                    [
                        new Resource { Uri = "docs://readme", Name = "readme", MimeType = "text/markdown", Description = "Project readme" }
                    ]
                }),
                ReadResourceHandler = (request, _) => ValueTask.FromResult(new ReadResourceResult
                {
                    Contents =
                    [
                        new TextResourceContents { Uri = request.Params!.Uri, MimeType = "text/markdown", Text = "# Readme" }
                    ]
                }),
                ListPromptsHandler = (_, _) => ValueTask.FromResult(new ListPromptsResult
                {
                    Prompts =
                    [
                        new McpPrompt
                        {
                            Name = "summarize",
                            Description = "Summarize a topic",
                            Arguments = [new PromptArgument { Name = "topic", Description = "What to summarize", Required = true }]
                        }
                    ]
                }),
                GetPromptHandler = (request, _) =>
                {
                    var topic = request.Params!.Arguments is { } args && args.TryGetValue("topic", out var value)
                        ? value.GetString()
                        : null;
                    ReceivedPromptTopic = topic;
                    return ValueTask.FromResult(new GetPromptResult
                    {
                        Messages = [new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = $"Summarize {topic}" } }]
                    });
                }
            }
        };

        var serverTransport = new StreamServerTransport(
            _clientToServer.Reader.AsStream(), _serverToClient.Writer.AsStream(), "docs-server", NullLoggerFactory.Instance);
        _server = McpServer.Create(serverTransport, options, NullLoggerFactory.Instance);
        _serverTask = _server.RunAsync(_serverCts.Token);

        var clientTransport = new StreamClientTransport(
            _clientToServer.Writer.AsStream(), _serverToClient.Reader.AsStream(), NullLoggerFactory.Instance);
        var client = await McpClient.CreateAsync(clientTransport);
        _adapter = new McpClientAdapter(client, "docs-server", NullLogger.Instance, prefixToolNameWithServer: false);
    }

    public async Task DisposeAsync()
    {
        if (_adapter != null)
        {
            await _adapter.DisposeAsync();
        }

        await _serverCts.CancelAsync();
        if (_server != null)
        {
            await _server.DisposeAsync();
        }

        if (_serverTask != null)
        {
            try
            {
                await _serverTask;
            }
            catch (OperationCanceledException)
            {
                // 服务器随测试结束被取消
            }
        }

        _serverCts.Dispose();
    }

    [Fact]
    public async Task ListResourcesAsync_ReturnsTheServersResources()
    {
        var resources = await _adapter!.ListResourcesAsync();

        resources.ShouldBe([new McpResourceInfo("docs://readme", "readme", "text/markdown", "Project readme")]);
    }

    [Fact]
    public async Task ReadResourceAsync_ReturnsTheResourceText()
    {
        var content = await _adapter!.ReadResourceAsync("docs://readme");

        content.ShouldBe(new McpResourceContent("# Readme", "text/markdown"));
    }

    [Fact]
    public async Task ListPromptsAsync_ReturnsThePromptWithItsArguments()
    {
        var prompts = await _adapter!.ListPromptsAsync();

        var prompt = prompts.ShouldHaveSingleItem();
        prompt.Name.ShouldBe("summarize");
        prompt.Description.ShouldBe("Summarize a topic");
        prompt.Arguments.ShouldNotBeNull();
        prompt.Arguments.ShouldBe([new McpPromptArgument("topic", "What to summarize", true)]);
    }

    [Fact]
    public async Task GetPromptAsync_ForwardsTheArgumentsAndReturnsTheMessages()
    {
        var result = await _adapter!.GetPromptAsync("summarize", new Dictionary<string, string> { ["topic"] = "MCP" });

        ReceivedPromptTopic.ShouldBe("MCP");
        result.ShouldNotBeNull();
        result.Messages.ShouldBe([new McpPromptMessage("user", "Summarize MCP")]);
    }
}
