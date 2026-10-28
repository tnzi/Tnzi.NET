using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Protocol;
using System.Text.Json.Nodes;
using Tnzi.AI.Mcp.Server;
using Tnzi.Exceptions;
using McpServerOptions = Tnzi.AI.Mcp.Options.McpServerOptions;

namespace Tnzi.AI.Tests.Mcp;

/// <summary>
/// 消费方自定义工具的完整形态（<see cref="McpToolDefinition"/>）：宣告的 schema、原样的参数、调用方、
/// 本次调用的 DI 作用域与取消令牌、原样的结果，以及与其它工具同一套限流 / 白名单 / 审计。
/// </summary>
/// <remarks>
/// ★ 此前自定义工具只有 <c>Func&lt;JsonElement, Task&lt;string&gt;&gt;</c> 一种 handler：拿不到取消令牌（运行被取消、连接断开后
/// 一个要跑十几分钟的工具照样跑完）、拿不到作用域服务、只能回一个字符串（发不出图片，也说不出「这是个错误结果」），
/// 调用方是谁只能去翻 <c>HttpContext.Items</c>，凭据里也没有派发这次运行的用户。
/// </remarks>
public class McpServerCustomToolTests
{
    private static readonly Guid RunId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid AgentId = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
    private static readonly Guid TenantX = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
    private static readonly Guid CreatorId = Guid.Parse("cccccccc-0000-0000-0000-000000000004");
    private static readonly Guid ThreadId = Guid.Parse("cccccccc-0000-0000-0000-000000000005");

    private const string RelaySchema = """
        {"type":"object","properties":{"device":{"type":"string","description":"Target device"},"command":{"type":"string"},
        "timeout_ms":{"type":"integer","minimum":1}},"required":["device","command"],"additionalProperties":false}
        """;

    private readonly SwitchableCurrentTenant _tenant = new();
    private readonly HttpContextAccessorStub _accessor = new();
    private readonly List<(string Tool, bool IsSuccess, string? Error)> _audited = [];

    private McpServerHost CreateHost(int rateLimitPerMinute = 0)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentTenant>(_tenant);
        services.AddScoped<ScopedProbe>();
        var serviceProvider = services.BuildServiceProvider();

        var analytics = new Mock<IMcpToolAnalyticsService>();
        analytics
            .Setup(a => a.RecordUsageAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<string, long, bool, string?, string?>((tool, _, success, error, _) => _audited.Add((tool, success, error)))
            .Returns(Task.CompletedTask);
        var auditServices = new ServiceCollection().AddSingleton(analytics.Object).BuildServiceProvider();

        var security = new McpServerSecurityMiddleware(
            new StaticOptionsMonitor<McpServerOptions>(new McpServerOptions
            {
                RateLimitPerMinute = rateLimitPerMinute,
                EnableAuditLog = false,
                EnableToolAnalytics = true
            }),
            NullLogger<McpServerSecurityMiddleware>.Instance,
            auditServices);

        return new McpServerHost(
            serviceProvider,
            new StaticOptionsMonitor<McpServerOptions>(new McpServerOptions { Enabled = true, RequireAuthentication = false }),
            NullLogger<McpServerHost>.Instance,
            security,
            _accessor);
    }

    private static McpToolDefinition Define(
        string name, Func<McpToolCallContext, CancellationToken, Task<CallToolResult>> handler, string schema = RelaySchema)
    {
        using var document = JsonDocument.Parse(schema);
        return new McpToolDefinition
        {
            Name = name,
            Description = "Runs a command on a device",
            InputSchema = document.RootElement.Clone(),
            Handler = handler
        };
    }

    private static CallToolResult Text(string text, bool isError = false)
        => new() { Content = [new TextContentBlock { Text = text }], IsError = isError };

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static bool SameJson(JsonElement actual, string expected)
        => JsonNode.DeepEquals(JsonNode.Parse(actual.GetRawText()), JsonNode.Parse(expected));

    private DefaultHttpContext ActAsRunScoped(params string[] allowedTools)
    {
        var context = new DefaultHttpContext();
        context.Items[McpServerSecurityMiddleware.CallerHashItemKey] = "run-caller";
        context.Items[McpServerSecurityMiddleware.CallerScopeItemKey] = new McpCallerScope(new RunScopedCredential
        {
            RunId = RunId,
            AgentId = AgentId,
            TenantId = TenantX,
            CreatorId = CreatorId,
            ThreadId = ThreadId,
            AllowedToolNames = allowedTools.Length == 0 ? ["*"] : allowedTools
        });
        _accessor.HttpContext = context;
        return context;
    }

    private DefaultHttpContext ActAsStaticKey()
    {
        var context = new DefaultHttpContext();
        context.Items[McpServerSecurityMiddleware.CallerHashItemKey] = "static-caller";
        context.Items[McpServerSecurityMiddleware.CallerScopeItemKey] = McpCallerScope.Unrestricted;
        _accessor.HttpContext = context;
        return context;
    }

    [Fact]
    public async Task ListTools_AdvertisesTheDeclaredSchemaExactly()
    {
        var host = CreateHost();
        host.ExposeTool(Define("relay", (_, _) => Task.FromResult(Text("ok"))));

        var tool = (await host.ListToolsAsync()).Single(t => t.Name == "relay");

        SameJson(tool.InputSchema, RelaySchema).ShouldBeTrue(tool.InputSchema.GetRawText());
        tool.Description.ShouldBe("Runs a command on a device");
    }

    [Fact]
    public async Task CallTool_HandsTheArgumentsObjectToTheHandlerUnchanged()
    {
        var host = CreateHost();
        JsonElement seen = default;
        host.ExposeTool(Define("relay", (call, _) =>
        {
            seen = call.Arguments.Clone();
            return Task.FromResult(Text("ok"));
        }));

        await host.CallToolAsync("relay", new Dictionary<string, JsonElement>
        {
            ["device"] = Json("\"dev-1\""),
            ["command"] = Json("\"ls -la\""),
            ["timeout_ms"] = Json("900000"),
            ["ratio"] = Json("1.50"),
            ["options"] = Json("""{"retries":[1,2],"verbose":true,"label":null}""")
        });

        SameJson(seen, """{"device":"dev-1","command":"ls -la","timeout_ms":900000,"ratio":1.50,"options":{"retries":[1,2],"verbose":true,"label":null}}""")
            .ShouldBeTrue(seen.GetRawText());
        // 原样：数字不经任何类型转换（1.50 不会变成 1.5）
        seen.GetProperty("ratio").GetRawText().ShouldBe("1.50");
    }

    [Fact]
    public async Task CallTool_WithoutArguments_HandsAnEmptyObject()
    {
        var host = CreateHost();
        JsonElement seen = default;
        host.ExposeTool(Define("relay", (call, _) =>
        {
            seen = call.Arguments.Clone();
            return Task.FromResult(Text("ok"));
        }));

        await host.CallToolAsync("relay", null);

        seen.ValueKind.ShouldBe(JsonValueKind.Object);
        seen.EnumerateObject().ShouldBeEmpty();
    }

    [Fact]
    public async Task Handler_SeesTheRunScopedCaller_IncludingWhoDispatchedTheRun()
    {
        var host = CreateHost();
        McpCallerScope? caller = null;
        string? toolName = null;
        host.ExposeTool(Define("relay", (call, _) =>
        {
            caller = call.Caller;
            toolName = call.ToolName;
            return Task.FromResult(Text("ok"));
        }));
        ActAsRunScoped();

        await host.CallToolAsync("relay", null);

        caller.ShouldNotBeNull();
        caller!.IsRunScoped.ShouldBeTrue();
        caller.RunId.ShouldBe(RunId);
        caller.AgentId.ShouldBe(AgentId);
        caller.TenantId.ShouldBe(TenantX);
        caller.CreatorId.ShouldBe(CreatorId);
        caller.ThreadId.ShouldBe(ThreadId, "a consumer tool learns which conversation it acts for without looking the run up");
        toolName.ShouldBe("relay");
    }

    [Fact]
    public async Task Handler_StaticKeyCaller_CarriesNoRunIdentity()
    {
        var host = CreateHost();
        McpCallerScope? caller = null;
        host.ExposeTool(Define("relay", (call, _) =>
        {
            caller = call.Caller;
            return Task.FromResult(Text("ok"));
        }));
        ActAsStaticKey();

        await host.CallToolAsync("relay", null);

        caller.ShouldNotBeNull();
        caller!.IsRunScoped.ShouldBeFalse();
        caller.RunId.ShouldBeNull();
        caller.AgentId.ShouldBeNull();
        caller.TenantId.ShouldBeNull();
        caller.CreatorId.ShouldBeNull();
        caller.ThreadId.ShouldBeNull();
    }

    [Fact]
    public async Task Handler_ResolvesScopedServicesFromAScopeOfItsOwn_ThatEndsWithTheCall()
    {
        var host = CreateHost();
        var probes = new List<ScopedProbe>();
        host.ExposeTool(Define("relay", (call, _) =>
        {
            var probe = call.Services.GetRequiredService<ScopedProbe>();
            probe.Disposed.ShouldBeFalse();
            probes.Add(probe);
            return Task.FromResult(Text("ok"));
        }));

        await host.CallToolAsync("relay", null);
        await host.CallToolAsync("relay", null);

        probes.Count.ShouldBe(2);
        probes[0].ShouldNotBeSameAs(probes[1], "every call gets its own scope");
        probes.ShouldAllBe(p => p.Disposed, "the scope is released once the handler returns");
    }

    [Fact]
    public async Task Handler_RunScopedCaller_RunsUnderTheCredentialsTenant()
    {
        var host = CreateHost();
        Guid? seen = Guid.Empty;
        host.ExposeTool(Define("relay", (call, _) =>
        {
            seen = call.Services.GetRequiredService<ICurrentTenant>().Id;
            return Task.FromResult(Text("ok"));
        }));
        ActAsRunScoped();

        await host.CallToolAsync("relay", null);

        seen.ShouldBe(TenantX);
    }

    [Fact]
    public async Task Handler_StaticKeyUnderAHeaderChosenTenant_RunsInTheHostScope()
    {
        // 匿名请求的 X-Tenant-Id 头经 TenantResolverMiddleware 切了环境租户：客户端可控，不是调用方身份
        var host = CreateHost();
        Guid? seen = Guid.Empty;
        host.ExposeTool(Define("relay", (call, _) =>
        {
            seen = call.Services.GetRequiredService<ICurrentTenant>().Id;
            return Task.FromResult(Text("ok"));
        }));
        ActAsStaticKey();

        using (_tenant.Change(TenantX))
        {
            await host.CallToolAsync("relay", null);
        }

        seen.ShouldBeNull();
    }

    [Fact]
    public async Task Handler_TokenFires_WhenTheCallIsCancelled()
    {
        var host = CreateHost();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedCancellation = false;
        host.ExposeTool(Define("relay", async (_, ct) =>
        {
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                observedCancellation = true;
                throw;
            }

            return Text("unreachable");
        }));
        using var cts = new CancellationTokenSource();

        var call = host.CallToolAsync("relay", null, cts.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(10)));
        observedCancellation.ShouldBeTrue();
        // 没有人在等结果了，但这次调用照样留下记录
        _audited.ShouldHaveSingleItem().IsSuccess.ShouldBeFalse();
        _audited[0].Error.ShouldNotBeNull().ShouldContain("cancelled");
    }

    [Fact]
    public async Task Handler_TokenFires_WhenTheClientDisconnects()
    {
        // 外部运行被取消或进程被杀：承载 tools/call 的 HTTP 请求中断，SDK 自己交来的令牌并不触发
        var host = CreateHost();
        using var aborted = new CancellationTokenSource();
        ActAsRunScoped().RequestAborted = aborted.Token;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.ExposeTool(Define("relay", async (_, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return Text("unreachable");
        }));

        var call = host.CallToolAsync("relay", null, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await aborted.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Handler_ResultIsSentAsIs_TextAndImageBlocks()
    {
        var host = CreateHost();
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02];
        host.ExposeTool(Define("screenshot", (_, _) => Task.FromResult(new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = "Captured the screen." },
                ImageContentBlock.FromBytes(png, "image/png")
            ]
        })));

        var result = await host.CallToolAsync("screenshot", null);

        result.IsError.ShouldNotBe(true);
        result.Content.Count.ShouldBe(2);
        result.Content[0].ShouldBeOfType<TextContentBlock>().Text.ShouldBe("Captured the screen.");
        var image = result.Content[1].ShouldBeOfType<ImageContentBlock>();
        image.MimeType.ShouldBe("image/png");
        image.DecodedData.ToArray().ShouldBe(png);
        result.StructuredContent.ShouldBeNull("the handler owns the result: nothing is sniffed into structured content");
        _audited.ShouldHaveSingleItem().IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Handler_ReturnsAnErrorResult_SentAsIsAndAuditedAsAFailure()
    {
        var host = CreateHost();
        host.ExposeTool(Define("relay", (_, _) => Task.FromResult(Text("Device dev-1 is offline.", isError: true))));

        var result = await host.CallToolAsync("relay", null);

        result.IsError.ShouldBe(true);
        result.Content.ShouldHaveSingleItem().ShouldBeOfType<TextContentBlock>().Text.ShouldBe("Device dev-1 is offline.");
        var audited = _audited.ShouldHaveSingleItem();
        audited.IsSuccess.ShouldBeFalse();
        audited.Error.ShouldBe("Device dev-1 is offline.");
    }

    [Fact]
    public async Task Handler_ErrorResultWithAHugeText_IsAuditedWithinTheColumnWidth()
    {
        var host = CreateHost();
        host.ExposeTool(Define("relay", (_, _) => Task.FromResult(Text(new string('x', 300_000), isError: true))));

        await host.CallToolAsync("relay", null);

        _audited.ShouldHaveSingleItem().Error!.Length.ShouldBeLessThanOrEqualTo(2000);
    }

    [Fact]
    public async Task Handler_ThrowsABusinessException_ItsMessageReachesTheClient()
    {
        var host = CreateHost();
        host.ExposeTool(Define("relay", (_, _) => throw new BusinessException("Device dev-1 is offline.", "DEVICE_OFFLINE", 409)));

        var result = await host.CallToolAsync("relay", null);

        result.IsError.ShouldBe(true);
        result.Content.ShouldHaveSingleItem().ShouldBeOfType<TextContentBlock>().Text.ShouldBe("Error: Device dev-1 is offline.");
        _audited.ShouldHaveSingleItem().IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public async Task Handler_ThrowsAnyOtherException_OnlyAGenericMessageReachesTheClient()
    {
        var host = CreateHost();
        host.ExposeTool(Define("relay", (_, _) => throw new InvalidOperationException("connection string: Host=db;Password=secret")));

        var result = await host.CallToolAsync("relay", null);

        result.IsError.ShouldBe(true);
        var text = result.Content.ShouldHaveSingleItem().ShouldBeOfType<TextContentBlock>().Text;
        text.ShouldBe("Error: An internal error occurred while processing the request.");
        text.ShouldNotContain("secret");
    }

    [Fact]
    public async Task RunScopedAllowList_AppliesToDefinedTools()
    {
        var host = CreateHost();
        var invoked = false;
        host.ExposeTool(Define("relay", (_, _) =>
        {
            invoked = true;
            return Task.FromResult(Text("ok"));
        }));
        ActAsRunScoped("some_other_tool");

        var listed = await host.ListToolsAsync();
        var result = await host.CallToolAsync("relay", null);

        listed.ShouldBeEmpty();
        result.IsError.ShouldBe(true);
        invoked.ShouldBeFalse();
    }

    [Fact]
    public async Task RateLimit_AppliesToDefinedTools()
    {
        var host = CreateHost(rateLimitPerMinute: 1);
        var invocations = 0;
        host.ExposeTool(Define("relay", (_, _) =>
        {
            invocations++;
            return Task.FromResult(Text("ok"));
        }));
        ActAsStaticKey();

        var first = await host.CallToolAsync("relay", null);
        var second = await host.CallToolAsync("relay", null);

        first.IsError.ShouldNotBe(true);
        second.IsError.ShouldBe(true);
        invocations.ShouldBe(1);
    }

    [Fact]
    public void Expose_RejectsAnInvalidName()
    {
        var host = CreateHost();

        Should.Throw<ArgumentException>(() => host.ExposeTool(Define("relay tool", (_, _) => Task.FromResult(Text("ok")))));
    }

    [Fact]
    public void Expose_RejectsASchemaWhoseTopLevelIsNotAnObject()
    {
        var host = CreateHost();

        Should.Throw<ArgumentException>(() =>
            host.ExposeTool(Define("relay", (_, _) => Task.FromResult(Text("ok")), schema: """{"type":"string"}""")));
    }

    [Fact]
    public async Task StringHandler_StillReturnsOneTextBlock_WithJsonAlsoAsStructuredContent()
    {
        // 字符串 handler 的结果形状不变：一个文本块，文本是 JSON 时同时放进 structuredContent
        var host = CreateHost();
        JsonElement seen = default;
        host.ExposeTool("lookup", "looks something up", payload =>
        {
            seen = payload.Clone();
            return Task.FromResult("""{"status":"ok"}""");
        });

        var result = await host.CallToolAsync("lookup", new Dictionary<string, JsonElement> { ["input"] = Json("\"abc\"") });

        SameJson(seen, """{"input":"abc"}""").ShouldBeTrue();
        result.Content.ShouldHaveSingleItem().ShouldBeOfType<TextContentBlock>().Text.ShouldBe("""{"status":"ok"}""");
        result.StructuredContent.ShouldNotBeNull();
    }

    private sealed class ScopedProbe : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
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
