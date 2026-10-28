using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Tnzi.AI.Infrastructure.Mcp;
using Tnzi.Modules;
using Tnzi.AI.Mcp.Server;
using Tnzi.AI.Mcp;

namespace Tnzi.AI.Tests;

public class McpServerHttpEndToEndTests
{
    [Fact]
    public async Task Endpoint_WithoutApiKey_ReturnsUnauthorized()
    {
        var app = await CreateAppAsync();

        try
        {
            using var client = CreateHttpClient(app);
            var response = await client.PostAsJsonAsync("/mcp", new
            {
                jsonrpc = "2.0",
                id = "1",
                method = "tools/list",
                @params = new { }
            });

            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Endpoint_WithApiKey_ListsAndCallsExposedTool()
    {
        var app = await CreateAppAsync();

        try
        {
            var host = app.Services.GetRequiredService<IMcpServerHost>();
            // 声明的 schema 就是客户端看到的、也是 handler 收到的形状：按 message 取值就要宣告 message
            using var schema = JsonDocument.Parse(
                """{"type":"object","properties":{"message":{"type":"string"}},"required":["message"]}""");
            host.ExposeTool(
                "echo",
                "Echoes the provided message",
                schema.RootElement,
                payload => Task.FromResult($"echo:{payload.GetProperty("message").GetString()}"));

            await using var factory = new McpClientFactory(NullLoggerFactory.Instance, NullLogger<McpClientFactory>.Instance);
            var adapter = await factory.GetOrCreateClientAsync(new McpServerConfig
            {
                Name = "local-http",
                ConnectionType = McpConnectionType.Http,
                Endpoint = new Uri(GetBaseAddress(app), "/mcp").ToString(),
                Headers = new Dictionary<string, string>
                {
                    [McpServerSecurityMiddleware.ApiKeyHeaderName] = "secret",
                    ["X-Tenant-Id"] = "tenant-a"
                }
            });

            var tools = await adapter.ListToolsAsync(CancellationToken.None);
            tools.Select(x => x.Name).ShouldContain("echo");

            var tool = tools.Single(x => x.Name == "echo");
            ((AIFunction)tool).JsonSchema.GetProperty("properties").TryGetProperty("message", out _)
                .ShouldBeTrue("the client calls the tool with the arguments the server advertised");

            // A real HTTP tools/call dispatches through McpServerHost.CallToolAsync → InvokeCustomToolAsync
            // (the one execution path with rate-limit/audit guards); the arguments object reaches the handler as is.
            var response = await InvokeToolTextAsync(tool, new Dictionary<string, object?>
            {
                ["message"] = "hello"
            });
            response.ShouldBe("echo:hello");

            // Re-invoke through the same guarded path with different input.
            var second = await InvokeToolTextAsync(tool, new Dictionary<string, object?>
            {
                ["message"] = "world"
            });
            second.ShouldBe("echo:world");
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Endpoint_DefinedTool_AdvertisesItsSchemaExactly_AndSendsTextAndImageBlocks()
    {
        var app = await CreateAppAsync();

        try
        {
            byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x07];
            var host = app.Services.GetRequiredService<IMcpServerHost>();
            host.ExposeTool(Define("screenshot", RelaySchema, (_, _) => Task.FromResult(new CallToolResult
            {
                Content =
                [
                    new TextContentBlock { Text = "Captured." },
                    ImageContentBlock.FromBytes(png, "image/png")
                ]
            })));

            await using var client = await ConnectAsync(app, StaticKeyHeaders);
            var tool = (await client.ListToolsAsync()).Single(t => t.Name == "screenshot");
            var result = await client.CallToolAsync("screenshot", new Dictionary<string, object?> { ["device"] = "dev-1", ["command"] = "capture" });

            JsonNode.DeepEquals(JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText()), JsonNode.Parse(RelaySchema))
                .ShouldBeTrue(tool.ProtocolTool.InputSchema.GetRawText());
            result.IsError.ShouldNotBe(true);
            result.Content.Count.ShouldBe(2);
            result.Content[0].ShouldBeOfType<TextContentBlock>().Text.ShouldBe("Captured.");
            var image = result.Content[1].ShouldBeOfType<ImageContentBlock>();
            image.MimeType.ShouldBe("image/png");
            image.DecodedData.ToArray().ShouldBe(png);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Endpoint_RunScopedBearer_HandlerSeesTheArgumentsAndTheRunThatCalled()
    {
        var app = await CreateAppAsync<TestAiMcpRunScopedStartupModule>();

        try
        {
            JsonElement seenArguments = default;
            McpCallerScope? seenCaller = null;
            var host = app.Services.GetRequiredService<IMcpServerHost>();
            host.ExposeTool(Define("relay", RelaySchema, (call, _) =>
            {
                seenArguments = call.Arguments.Clone();
                seenCaller = call.Caller;
                return Task.FromResult(new CallToolResult { Content = [new TextContentBlock { Text = "relayed" }] });
            }));
            host.ExposeTool(Define("not_for_runs", RelaySchema, (_, _) => Task.FromResult(new CallToolResult())));

            await using var client = await ConnectAsync(app, new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {StubRunCredentialValidator.Token}"
            });
            var names = (await client.ListToolsAsync()).Select(t => t.Name).ToList();
            var result = await client.CallToolAsync("relay", new Dictionary<string, object?>
            {
                ["device"] = "dev-1",
                ["command"] = "ping",
                ["timeout_ms"] = 900000
            });

            names.ShouldBe(["relay"], "the credential's allow-list narrows tools/list");
            result.Content.ShouldHaveSingleItem().ShouldBeOfType<TextContentBlock>().Text.ShouldBe("relayed");
            JsonNode.DeepEquals(JsonNode.Parse(seenArguments.GetRawText()), JsonNode.Parse("""{"device":"dev-1","command":"ping","timeout_ms":900000}"""))
                .ShouldBeTrue(seenArguments.GetRawText());
            seenCaller.ShouldNotBeNull();
            seenCaller!.IsRunScoped.ShouldBeTrue();
            seenCaller.RunId.ShouldBe(StubRunCredentialValidator.RunId);
            seenCaller.AgentId.ShouldBe(StubRunCredentialValidator.AgentId);
            seenCaller.CreatorId.ShouldBe(StubRunCredentialValidator.CreatorId);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Endpoint_ClientCancelsTheCall_TheHandlersTokenFires()
    {
        var app = await CreateAppAsync();

        try
        {
            var (started, cancelled) = ExposeSlowTool(app);
            await using var client = await ConnectAsync(app, StaticKeyHeaders);
            using var cts = new CancellationTokenSource();

            var call = client.CallToolAsync("slow", new Dictionary<string, object?>(), cancellationToken: cts.Token).AsTask();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await cts.CancelAsync();

            await Should.ThrowAsync<OperationCanceledException>(() => call);
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Endpoint_CancelledNotification_TheHandlersTokenFires()
    {
        // 协议层的取消：请求还开着，客户端另发一条 notifications/cancelled 点名它
        var app = await CreateAppAsync();

        try
        {
            var (started, cancelled) = ExposeSlowTool(app);
            using var http = CreateHttpClient(app);
            http.DefaultRequestHeaders.Add(McpServerSecurityMiddleware.ApiKeyHeaderName, "secret");
            var (sessionId, version) = await InitializeRawSessionAsync(http);

            using var callConnection = new CancellationTokenSource();
            var call = Task.Run(async () =>
            {
                using var response = await PostRpcAsync(http, sessionId, version,
                    """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"slow","arguments":{}}}""", callConnection.Token);
                return await response.Content.ReadAsStringAsync(callConnection.Token);
            });
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var notification = await PostRpcAsync(http, sessionId, version,
                """{"jsonrpc":"2.0","method":"notifications/cancelled","params":{"requestId":2,"reason":"no longer needed"}}""",
                CancellationToken.None);

            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            notification.IsSuccessStatusCode.ShouldBeTrue();

            // 被取消的请求不再应答，承载它的那条 POST 一直开着；关掉它，停机才不必等它超时
            await callConnection.CancelAsync();
            await Should.ThrowAsync<OperationCanceledException>(() => call);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Endpoint_ClientDisconnectsMidCall_TheHandlersTokenFires()
    {
        // 外部运行被取消、CLI 进程被杀：连接直接断开，客户端不会再发 notifications/cancelled。
        // 用裸 HTTP 走协议，确保会话本身还活着：断的只有承载这次 tools/call 的那个请求
        var app = await CreateAppAsync();

        try
        {
            var (started, cancelled) = ExposeSlowTool(app);
            using var http = CreateHttpClient(app);
            http.DefaultRequestHeaders.Add(McpServerSecurityMiddleware.ApiKeyHeaderName, "secret");
            var (sessionId, version) = await InitializeRawSessionAsync(http);

            using var cts = new CancellationTokenSource();
            var call = Task.Run(async () =>
            {
                using var response = await PostRpcAsync(http, sessionId, version,
                    """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"slow","arguments":{}}}""", cts.Token);
                return await response.Content.ReadAsStringAsync(cts.Token);
            });
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await cts.CancelAsync();

            await Should.ThrowAsync<OperationCanceledException>(() => call);
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private const string RelaySchema = """
        {"type":"object","properties":{"device":{"type":"string"},"command":{"type":"string"},"timeout_ms":{"type":"integer"}},
        "required":["device","command"],"additionalProperties":false}
        """;

    private static readonly Dictionary<string, string> StaticKeyHeaders = new()
    {
        [McpServerSecurityMiddleware.ApiKeyHeaderName] = "secret"
    };

    private static McpToolDefinition Define(
        string name, string schema, Func<McpToolCallContext, CancellationToken, Task<CallToolResult>> handler)
    {
        using var document = JsonDocument.Parse(schema);
        return new McpToolDefinition
        {
            Name = name,
            Description = $"Test tool {name}",
            InputSchema = document.RootElement.Clone(),
            Handler = handler
        };
    }

    /// <summary>一个一直等到被取消的工具：返回「已开始」与「观察到取消」两个信号。</summary>
    private static (TaskCompletionSource Started, TaskCompletionSource Cancelled) ExposeSlowTool(WebApplication app)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        app.Services.GetRequiredService<IMcpServerHost>().ExposeTool(Define("slow", """{"type":"object"}""", async (_, ct) =>
        {
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                cancelled.TrySetResult();
                throw;
            }

            return new CallToolResult();
        }));
        return (started, cancelled);
    }

    private static async Task<McpClient> ConnectAsync(WebApplication app, Dictionary<string, string> headers)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(GetBaseAddress(app), "/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = headers
        }, NullLoggerFactory.Instance);
        return await McpClient.CreateAsync(transport, loggerFactory: NullLoggerFactory.Instance);
    }

    /// <summary>不经 SDK 客户端走完初始化握手，返回会话 id 与协商出的协议版本。</summary>
    private static async Task<(string SessionId, string? ProtocolVersion)> InitializeRawSessionAsync(HttpClient http)
    {
        using var initialize = await PostRpcAsync(http, null, null,
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"raw-test","version":"1"}}}""",
            CancellationToken.None);
        initialize.EnsureSuccessStatusCode();
        var sessionId = initialize.Headers.GetValues("Mcp-Session-Id").Single();
        var version = ReadRpcMessage(await initialize.Content.ReadAsStringAsync())
            .GetProperty("result").GetProperty("protocolVersion").GetString();

        using var initialized = await PostRpcAsync(http, sessionId, version,
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""", CancellationToken.None);
        initialized.EnsureSuccessStatusCode();
        return (sessionId, version);
    }

    private static Task<HttpResponseMessage> PostRpcAsync(
        HttpClient http, string? sessionId, string? protocolVersion, string body, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (sessionId is not null)
        {
            request.Headers.Add("Mcp-Session-Id", sessionId);
        }

        if (protocolVersion is not null)
        {
            request.Headers.Add("MCP-Protocol-Version", protocolVersion);
        }

        return http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    /// <summary>读出响应里的 JSON-RPC 消息：直接的 JSON，或 SSE 的第一条 <c>data:</c>。</summary>
    private static JsonElement ReadRpcMessage(string body)
    {
        var json = body.TrimStart().StartsWith('{')
            ? body
            : body.Split('\n').Select(l => l.Trim()).First(l => l.StartsWith("data:", StringComparison.Ordinal))["data:".Length..];
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static Task<WebApplication> CreateAppAsync() => CreateAppAsync<TestAiMcpStartupModule>();

    private static async Task<WebApplication> CreateAppAsync<TStartupModule>()
        where TStartupModule : TnziApplicationModule
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateOnBuild = false;
            options.ValidateScopes = false;
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:AutoDiscoverDbContexts"] = "false",
            ["AspNetCore:EnableForwardedHeaders"] = "false",
            ["AI:DefaultProvider"] = "Test",
            ["AI:Providers:Test:Enabled"] = "true",
            ["AI:Providers:Test:ApiKey"] = "test-key",
            ["AI:Providers:Test:DefaultModel"] = "test-model",
            ["AI:Providers:DeepSeek:Enabled"] = "false",
            ["AI:McpServer:Enabled"] = "true",
            ["AI:McpServer:Endpoint"] = "/mcp",
            ["AI:McpServer:RequireAuthentication"] = "true",
            ["AI:McpServer:AllowedApiKeys:0"] = "secret",
            ["AI:McpServer:EnableAuditLog"] = "false",
            ["AI:McpServer:RateLimitPerMinute"] = "0"
            // Query-string API keys stay off (the server default): McpClientFactory sends the key
            // as the X-Api-Key request header, never in the URL.
        });

        var app = await TnziApp.CreateAsync<TStartupModule>(builder);
        await app.StartAsync();
        return app;
    }

    private static HttpClient CreateHttpClient(WebApplication app)
    {
        return new HttpClient
        {
            BaseAddress = GetBaseAddress(app)
        };
    }

    private static Uri GetBaseAddress(WebApplication app)
    {
        var server = app.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>();
        var address = addresses?.Addresses.SingleOrDefault();
        address.ShouldNotBeNull();
        return new Uri(address!);
    }

    private static async Task<string> InvokeToolTextAsync(object tool, Dictionary<string, object?> arguments)
    {
        var method = ResolveInvokeAsyncMethod(tool.GetType());
        var parameters = method.GetParameters();
        var invokeArguments = new List<object?>();
        if (parameters.Length > 0 && parameters[0].ParameterType != typeof(CancellationToken))
        {
            invokeArguments.Add(BuildInvokePayload(parameters[0].ParameterType, arguments));
        }
        if (parameters.Length > 0 && parameters[^1].ParameterType == typeof(CancellationToken))
        {
            invokeArguments.Add(CancellationToken.None);
        }

        var invokedTask = method.Invoke(tool, invokeArguments.ToArray());
        if (invokedTask is ValueTask valueTask)
        {
            await valueTask.ConfigureAwait(false);
            return string.Empty;
        }
        if (invokedTask is { } valueTaskLike
            && valueTaskLike.GetType().IsValueType
            && valueTaskLike.GetType().IsGenericType
            && valueTaskLike.GetType().GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var asTaskMethod = valueTaskLike.GetType().GetMethod("AsTask", BindingFlags.Instance | BindingFlags.Public);
            asTaskMethod.ShouldNotBeNull();
            var genericTask = asTaskMethod!.Invoke(valueTaskLike, null) as Task;
            if (genericTask == null)
            {
                throw new InvalidOperationException("ValueTask tool invocation did not return a Task.");
            }
            await genericTask!.ConfigureAwait(false);
            var genericResult = genericTask.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public)?.GetValue(genericTask);
            if (genericResult is string genericText)
            {
                return genericText;
            }
            if (genericResult != null)
            {
                var textProperty = genericResult.GetType().GetProperty("Text", BindingFlags.Instance | BindingFlags.Public);
                if (textProperty?.GetValue(genericResult) is string resultText)
                {
                    return resultText;
                }
            }
        }

        var task = invokedTask as Task;
        if (task == null)
        {
            throw new InvalidOperationException("Tool invocation did not return a Task.");
        }
        await task!.ConfigureAwait(false);

        var resultProperty = task.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public);
        var result = resultProperty?.GetValue(task);
        if (result is string text)
        {
            return text;
        }

        if (result != null)
        {
            var textProperty = result.GetType().GetProperty("Text", BindingFlags.Instance | BindingFlags.Public);
            if (textProperty?.GetValue(result) is string resultText)
            {
                return resultText;
            }
        }

        throw new InvalidOperationException($"Tool '{tool.GetType().FullName}' did not produce a text result.");
    }

    private static MethodInfo ResolveInvokeAsyncMethod(Type toolType)
    {
        var methods = toolType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => string.Equals(method.Name, "InvokeAsync", StringComparison.Ordinal))
            .Where(method =>
            {
                var parameters = method.GetParameters();
                return parameters.Length <= 2
                    && (parameters.Length == 0 || parameters[^1].ParameterType == typeof(CancellationToken) || parameters.Length == 1);
            })
            .ToList();

        methods.Count.ShouldBeGreaterThan(0);
        return methods
            .OrderByDescending(method => method.GetParameters().Length)
            .First();
    }

    private static object BuildInvokePayload(Type payloadType, Dictionary<string, object?> arguments)
    {
        if (payloadType == typeof(AIFunctionArguments))
        {
            return new AIFunctionArguments(arguments);
        }

        if (payloadType == typeof(JsonElement))
        {
            return JsonSerializer.SerializeToElement(arguments);
        }

        if (payloadType == typeof(string))
        {
            return JsonSerializer.Serialize(arguments);
        }

        if (typeof(IDictionary<string, object?>).IsAssignableFrom(payloadType)
            || typeof(IReadOnlyDictionary<string, object?>).IsAssignableFrom(payloadType))
        {
            return arguments;
        }

        if (typeof(IDictionary<string, JsonElement>).IsAssignableFrom(payloadType)
            || typeof(IReadOnlyDictionary<string, JsonElement>).IsAssignableFrom(payloadType))
        {
            return arguments.ToDictionary(
                kvp => kvp.Key,
                kvp => JsonSerializer.SerializeToElement(kvp.Value),
                StringComparer.OrdinalIgnoreCase);
        }

        return JsonSerializer.SerializeToElement(arguments);
    }
}

[DependsOn(typeof(AIMcpModule))]
internal sealed class TestAiMcpStartupModule : TnziApplicationModule
{
}

/// <summary>同上，另外接受一枚固定的运行范围凭据（外部运行回写的那条认证路径）。</summary>
[DependsOn(typeof(AIMcpModule))]
internal sealed class TestAiMcpRunScopedStartupModule : TnziApplicationModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddSingleton<IRunScopedCredentialValidator, StubRunCredentialValidator>();
        return Task.CompletedTask;
    }
}

internal sealed class StubRunCredentialValidator : IRunScopedCredentialValidator
{
    public const string Token = "tnzi-run_e2e-token";
    public static readonly Guid RunId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
    public static readonly Guid AgentId = Guid.Parse("dddddddd-0000-0000-0000-000000000002");
    public static readonly Guid CreatorId = Guid.Parse("dddddddd-0000-0000-0000-000000000003");

    public Task<RunScopedCredential?> ValidateAsync(string token, CancellationToken cancellationToken = default)
        => Task.FromResult(token == Token
            ? new RunScopedCredential { RunId = RunId, AgentId = AgentId, CreatorId = CreatorId, AllowedToolNames = ["relay"] }
            : null);
}
