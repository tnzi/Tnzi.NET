namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// ACP 适配器按 CLI 自己报的能力办事：图片块、会话选项里的模型与推理强度、握手报的能力交给使用方、需要登录的错误。
/// </summary>
/// <remarks>
/// 会话选项的形状按 codex-acp 2.5 与 Kimi Code 2.4 包内的代码写：codex 的推理强度 id 是 <c>reasoning_effort</c>，
/// Kimi 的是 <c>thinking</c>，两者的 category 都是 <c>thought_level</c>，适配器只认 category。
/// </remarks>
public class AcpSessionOptionsTests
{
    private const string CodexOptions = """
        [
          {"id":"model","name":"Model","category":"model","type":"select","currentValue":"gpt-5.5",
           "options":[{"value":"gpt-5.5","name":"5.5"},{"value":"gpt-5.5-mini","name":"5.5 Mini"}]},
          {"id":"reasoning_effort","name":"Reasoning effort","category":"thought_level","type":"select","currentValue":"medium",
           "options":[{"value":"low","name":"Low"},{"value":"medium","name":"Medium"},{"value":"high","name":"High"}]}
        ]
        """;

    private const string KimiOptions = """
        [
          {"type":"select","id":"thinking","name":"Thinking","category":"thought_level","currentValue":"on",
           "options":[{"value":"off","name":"Thinking Off"},{"value":"on","name":"Thinking On"}]}
        ]
        """;

    private const string CodexInitialize = """
        {"protocolVersion":1,"agentInfo":{"name":"@agentclientprotocol/codex-acp","version":"2.5.0"},
         "agentCapabilities":{"loadSession":true,"promptCapabilities":{"embeddedContext":true,"image":true},
           "sessionCapabilities":{"resume":{},"list":{}},"mcpCapabilities":{"acp":false,"http":true,"sse":false}},
         "authMethods":[]}
        """;

    private static CliAgentLaunchContext Context(string provider = "codex") => new()
    {
        Provider = CliBuiltInProviders.All[provider],
        ExecutablePath = "/nonexistent/" + provider,
        Prompt = "hello",
        WorkingDirectory = "/tmp/does-not-need-to-exist",
        HandshakeTimeout = TimeSpan.FromSeconds(5)
    };

    /// <summary>一个按方法名作答的 ACP 服务端；<paramref name="answer"/> 可以改写某个方法的应答（返回 null = 用缺省）。</summary>
    private static Func<string, IEnumerable<string>> Server(
        string initialize = CodexInitialize, string? configOptions = CodexOptions, Func<string, string, string?>? answer = null)
        => line =>
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("method", out var methodElement) || !root.TryGetProperty("id", out var idElement))
            {
                return [];
            }

            var method = methodElement.GetString()!;
            var id = idElement.GetRawText();
            if (answer?.Invoke(method, id) is { } custom)
            {
                return [custom];
            }

            var options = configOptions is null ? string.Empty : $",\"configOptions\":{configOptions}";
            return method switch
            {
                "initialize" => [Ok(id, initialize)],
                "session/new" => [Ok(id, $"{{\"sessionId\":\"acp-1\"{options}}}")],
                "session/set_config_option" => [Ok(id, configOptions is null ? "{}" : $"{{\"configOptions\":{configOptions}}}")],
                "session/set_model" => [Ok(id, "{}")],
                "session/prompt" => [Ok(id, """{"stopReason":"end_turn","usage":{"totalTokens":30,"inputTokens":20,"outputTokens":10,"cachedReadTokens":5,"cachedWriteTokens":3}}""")],
                _ => []
            };
        };

    /// <summary>一次应答里拆成几行 stdout 时的分隔符（JSON 里不会出现）。</summary>
    private const char Separator = '\u0001';

    private static string Ok(string id, string resultJson) => $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{resultJson}}}";

    private static async Task<(AcpAdapter Adapter, FakeCliAgentTransport Transport)> RunAsync(
        CliAgentLaunchContext context, Func<string, IEnumerable<string>> server)
    {
        var adapter = new AcpAdapter(NullLogger<AcpAdapter>.Instance);
        var transport = new FakeCliAgentTransport([], onWrite: server);
        await foreach (var _ in adapter.RunAsync(transport, context, CancellationToken.None))
        {
        }

        return (adapter, transport);
    }

    private static List<JsonElement> Requests(FakeCliAgentTransport transport, string method)
        => transport.Written
            .Select(w => JsonDocument.Parse(w).RootElement)
            .Where(e => e.TryGetProperty("method", out var m) && m.GetString() == method)
            .ToList();

    [Fact]
    public async Task ReasoningEffort_IsSetThroughTheThoughtLevelSessionOption()
    {
        var (adapter, transport) = await RunAsync(Context() with { ThinkingLevel = "high" }, Server());

        var set = Requests(transport, "session/set_config_option").Single().GetProperty("params");
        set.GetProperty("configId").GetString().ShouldBe("reasoning_effort");
        set.GetProperty("value").GetString().ShouldBe("high");
        set.GetProperty("sessionId").GetString().ShouldBe("acp-1");
        adapter.GetResult(new CliSessionOutcome { ExitCode = 0 }).Status.ShouldBe(CliRunStatus.Completed);
    }

    [Fact]
    public async Task ReasoningEffort_FindsTheOptionByCategoryNotById()
    {
        // Kimi Code 的推理强度叫 thinking：认 id 的话这一项会被当成「不支持」
        var (_, transport) = await RunAsync(Context("kimi") with { ThinkingLevel = "off" }, Server(configOptions: KimiOptions));

        var set = Requests(transport, "session/set_config_option").Single().GetProperty("params");
        set.GetProperty("configId").GetString().ShouldBe("thinking");
        set.GetProperty("value").GetString().ShouldBe("off");
    }

    [Fact]
    public async Task ReasoningEffort_NotOffered_FailsTheRunBeforeThePrompt()
    {
        // 静默回落到运行时的缺省强度，用户以为自己选的生效了
        var (adapter, transport) = await RunAsync(Context() with { ThinkingLevel = "xhigh" }, Server());

        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 0 });
        result.Status.ShouldBe(CliRunStatus.Failed);
        result.Error!.ShouldContain("reasoning effort 'xhigh'");
        result.Error!.ShouldContain("low, medium, high");
        Requests(transport, "session/prompt").ShouldBeEmpty();
        Requests(transport, "session/set_config_option").ShouldBeEmpty();
    }

    [Fact]
    public async Task Model_IsSetThroughTheModelSessionOption_BeforeTheReasoningEffort()
    {
        var (_, transport) = await RunAsync(Context() with { Model = "gpt-5.5-mini", ThinkingLevel = "low" }, Server());

        var sets = Requests(transport, "session/set_config_option").Select(r => r.GetProperty("params").GetProperty("configId").GetString()).ToList();
        sets.ShouldBe(["model", "reasoning_effort"]);
        Requests(transport, "session/set_model").ShouldBeEmpty();
    }

    [Fact]
    public async Task Model_WithoutAModelSessionOption_FallsBackToSetModel()
    {
        var (_, transport) = await RunAsync(Context("kimi") with { Model = "kimi-k3" }, Server(configOptions: KimiOptions));

        Requests(transport, "session/set_model").Single().GetProperty("params").GetProperty("modelId").GetString().ShouldBe("kimi-k3");
    }

    [Fact]
    public async Task Images_GoAsImageBlocksBeforeTheText()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var context = Context() with { Attachments = [new CliPromptAttachment("image/png", "shot.png", png)] };

        var (adapter, transport) = await RunAsync(context, Server());

        var prompt = Requests(transport, "session/prompt").Single().GetProperty("params").GetProperty("prompt").EnumerateArray().ToList();
        prompt.Count.ShouldBe(2);
        prompt[0].GetProperty("type").GetString().ShouldBe("image");
        prompt[0].GetProperty("mimeType").GetString().ShouldBe("image/png");
        prompt[0].GetProperty("data").GetString().ShouldBe(Convert.ToBase64String(png));
        prompt[1].GetProperty("type").GetString().ShouldBe("text");
        prompt[1].GetProperty("text").GetString().ShouldBe("hello");
        adapter.GetResult(new CliSessionOutcome { ExitCode = 0 }).Status.ShouldBe(CliRunStatus.Completed);
    }

    [Fact]
    public async Task Images_ToARuntimeThatSaysItTakesNone_FailTheRunInsteadOfSendingTheTextAlone()
    {
        const string noImages = """{"agentCapabilities":{"promptCapabilities":{"image":false}}}""";
        var context = Context() with { Attachments = [new CliPromptAttachment("image/png", null, [1, 2, 3])] };

        var (adapter, transport) = await RunAsync(context, Server(initialize: noImages));

        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 0 });
        result.Status.ShouldBe(CliRunStatus.Failed);
        result.FailureReason.ShouldBe(CliRunFailureReason.UnsupportedInput);
        Requests(transport, "session/new").ShouldBeEmpty();
        Requests(transport, "session/prompt").ShouldBeEmpty();
    }

    [Fact]
    public async Task OnlyAnImage_SendsNoEmptyTextBlock()
    {
        var context = Context() with { Prompt = "", Attachments = [new CliPromptAttachment("image/jpeg", null, [1])] };

        var (_, transport) = await RunAsync(context, Server());

        var prompt = Requests(transport, "session/prompt").Single().GetProperty("params").GetProperty("prompt").EnumerateArray().ToList();
        prompt.Single().GetProperty("type").GetString().ShouldBe("image");
    }

    [Fact]
    public async Task Capabilities_ReportedAtTheHandshake_AreHandedToTheConsumer()
    {
        var (adapter, _) = await RunAsync(Context(), Server());

        var capabilities = adapter.GetResult(new CliSessionOutcome { ExitCode = 0 }).Capabilities!;
        capabilities.AgentName.ShouldBe("@agentclientprotocol/codex-acp");
        capabilities.AgentVersion.ShouldBe("2.5.0");
        capabilities.Images.ShouldBeTrue();
        capabilities.Audio.ShouldBeFalse();
        capabilities.EmbeddedContext.ShouldBeTrue();
        capabilities.ResumeSession.ShouldBeTrue();
        capabilities.McpHttp.ShouldBeTrue();
        capabilities.McpSse.ShouldBeFalse();
        capabilities.ReasoningEfforts.ShouldBe(["low", "medium", "high"]);
        capabilities.Models.ShouldBe(["gpt-5.5", "gpt-5.5-mini"]);
    }

    [Fact]
    public async Task Capabilities_FollowAConfigOptionUpdate()
    {
        // 换模型后运行时重报会话选项：能力表里的推理强度跟着变
        const string update = """{"jsonrpc":"2.0","method":"session/update","params":{"sessionId":"acp-1","update":{"sessionUpdate":"config_option_update","configOptions":[{"id":"reasoning_effort","category":"thought_level","type":"select","currentValue":"low","options":[{"value":"low","name":"Low"}]}]}}}""";
        var server = Server(answer: (method, id) => method == "session/prompt"
            ? update + Separator + Ok(id, """{"stopReason":"end_turn"}""")
            : null);

        var (adapter, _) = await RunAsync(Context(), line => server(line).SelectMany(l => l.Split(Separator)));

        adapter.GetResult(new CliSessionOutcome { ExitCode = 0 }).Capabilities!.ReasoningEfforts.ShouldBe(["low"]);
    }

    [Fact]
    public async Task Usage_ReadsTheProtocolsCachedWriteTokens()
    {
        var (adapter, _) = await RunAsync(Context(), Server());

        var usage = adapter.GetResult(new CliSessionOutcome { ExitCode = 0 }).Usage.Values.Single();
        usage.InputTokens.ShouldBe(20);
        usage.OutputTokens.ShouldBe(10);
        usage.CacheReadTokens.ShouldBe(5);
        usage.CacheWriteTokens.ShouldBe(3);
        usage.ReportedCostUsd.ShouldBeNull("ACP reports no per-turn cost; the consumer prices the tokens");
    }

    [Fact]
    public async Task AuthenticationRequired_IsAnAuthenticationFailure()
    {
        // codex-acp 没有可用的登录时 session/new 回 -32000
        var server = Server(answer: (method, id) => method == "session/new"
            ? $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"error\":{{\"code\":-32000,\"message\":\"Authentication required\"}}}}"
            : null);

        var (adapter, _) = await RunAsync(Context(), server);

        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 0 });
        result.Status.ShouldBe(CliRunStatus.Failed);
        result.FailureReason.ShouldBe(CliRunFailureReason.AuthenticationFailed);
    }

    [Fact]
    public void CodexAndKimi_TakeImagesButNotDocuments()
    {
        foreach (var key in new[] { "codex", "kimi" })
        {
            var provider = CliBuiltInProviders.All[key];
            provider.Protocol.ShouldBe(CliAgentProtocol.Acp);
            provider.Attachments.Accepts("image/png").ShouldBeTrue(key);
            provider.Attachments.Accepts("application/pdf").ShouldBeFalse(key);
        }

        CliBuiltInProviders.All["claude"].Attachments.Accepts("application/pdf").ShouldBeTrue();
        new CliAgentLaunchContext
        {
            Provider = CliBuiltInProviders.All["codex"], ExecutablePath = "/x/codex-acp", Prompt = "p", WorkingDirectory = "/w"
        }.Provider.LaunchArgs.ShouldBeEmpty("codex-acp is itself the ACP server");
    }
}
