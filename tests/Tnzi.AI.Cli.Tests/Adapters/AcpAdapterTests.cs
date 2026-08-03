namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// ACP 适配器的会话生命周期、事件归一化与权限应答。
/// </summary>
public class AcpAdapterTests
{
    private static CliAgentLaunchContext Context(string? model = null, string? resume = null) => new()
    {
        Provider = CliBuiltInProviders.All["kimi"],
        ExecutablePath = "/nonexistent/kimi",
        Prompt = "hello",
        WorkingDirectory = "/tmp/does-not-need-to-exist",
        Model = model,
        ResumeSessionId = resume,
        HandshakeTimeout = TimeSpan.FromSeconds(5)
    };

    private static AcpAdapter Adapter() => new(NullLogger<AcpAdapter>.Instance);

    /// <summary>
    /// 一个最小的 ACP 服务端：按方法名回响应，可选地插入 session/update 通知。
    /// </summary>
    private static Func<string, IEnumerable<string>> Server(
        IReadOnlyList<string>? updatesBeforePromptResponse = null,
        bool failPromptWithSessionNotFound = false,
        bool failSetModel = false)
    {
        return line =>
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("method", out var methodElement))
            {
                return [];
            }

            var method = methodElement.GetString();
            var id = root.TryGetProperty("id", out var idElement) ? idElement.GetRawText() : null;
            if (id is null)
            {
                return [];
            }

            return method switch
            {
                "initialize" => [Ok(id, """{"agentCapabilities":{"mcpCapabilities":{"http":true}}}""")],
                "session/new" => [Ok(id, """{"sessionId":"acp-1"}""")],
                "session/resume" => [Ok(id, """{"sessionId":"acp-1"}""")],
                "session/set_model" => failSetModel
                    ? [Error(id, "No session found with id")]
                    : [Ok(id, "{}")],
                "session/prompt" => failPromptWithSessionNotFound
                    ? [Error(id, "No session found with id acp-1")]
                    : [
                        .. updatesBeforePromptResponse ?? [],
                        Ok(id, """{"stopReason":"end_turn","usage":{"inputTokens":11,"outputTokens":7}}""")
                      ],
                _ => []
            };
        };
    }

    private static string Ok(string id, string resultJson)
        => $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{resultJson}}}";

    private static string Error(string id, string data)
        => $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"error\":{{\"code\":-32603,\"message\":\"Internal error\",\"data\":\"{data}\"}}}}";

    private static async Task<List<CliAgentEvent>> DrainAsync(
        AcpAdapter adapter, FakeCliAgentTransport transport, CliAgentLaunchContext context)
    {
        var events = new List<CliAgentEvent>();
        await foreach (var evt in adapter.RunAsync(transport, context, CancellationToken.None))
        {
            events.Add(evt);
        }

        return events;
    }

    [Fact]
    public void BuildProcess_AppendsProviderLaunchArgsAndDropsProtocolSubcommandFromCustomArgs()
    {
        var spec = Adapter().BuildProcess(Context() with { CustomArgs = ["acp", "--verbose"] });

        // "acp" 是协议契约参数：由描述表提供，用户不能重复或改写。
        spec.Arguments.Count(a => a == "acp").ShouldBe(1);
        spec.Arguments.ShouldContain("--verbose");
    }

    [Fact]
    public async Task Run_DrivesFullSessionLifecycle()
    {
        var transport = new FakeCliAgentTransport([], onWrite: Server(
        [
            """{"jsonrpc":"2.0","method":"session/update","params":{"sessionId":"acp-1","update":{"sessionUpdate":"agent_message_chunk","content":{"type":"text","text":"Answer"}}}}"""
        ]));

        var adapter = Adapter();
        var events = await DrainAsync(adapter, transport, Context());

        var methods = transport.Written
            .Select(w => JsonDocument.Parse(w).RootElement)
            .Where(e => e.TryGetProperty("method", out _))
            .Select(e => e.GetProperty("method").GetString())
            .ToList();

        methods.ShouldContain("initialize");
        methods.ShouldContain("session/new");
        methods.ShouldContain("session/prompt");

        events.ShouldContain(e => e.Type == CliAgentEventType.Text && e.Content == "Answer");

        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 0 });
        result.Status.ShouldBe(CliRunStatus.Completed);
        result.SessionId.ShouldBe("acp-1");
        result.Usage.Values.Single().InputTokens.ShouldBe(11);
    }

    [Fact]
    public async Task Run_ExtractsDeliverableAsTextAfterTheLastToolCall()
    {
        // ACP 把过程叙述与最终答案发成同一种 chunk，唯一边界是工具调用。
        var transport = new FakeCliAgentTransport([], onWrite: Server(
        [
            """{"jsonrpc":"2.0","method":"session/update","params":{"update":{"sessionUpdate":"agent_message_chunk","content":{"type":"text","text":"Let me check the logs first. "}}}}""",
            """{"jsonrpc":"2.0","method":"session/update","params":{"update":{"sessionUpdate":"tool_call","toolCallId":"t1","title":"Run command: cat log","rawInput":{"command":"cat log"}}}}""",
            """{"jsonrpc":"2.0","method":"session/update","params":{"update":{"sessionUpdate":"tool_call_update","toolCallId":"t1","status":"completed","rawOutput":"log contents"}}}""",
            """{"jsonrpc":"2.0","method":"session/update","params":{"update":{"sessionUpdate":"agent_message_chunk","content":{"type":"text","text":"The conclusion is X."}}}}"""
        ]));

        var adapter = Adapter();
        await DrainAsync(adapter, transport, Context());

        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 0 });
        result.Output.ShouldBe("The conclusion is X.");
        // 完整文本流必须保留：错误嗅探要读每一个 chunk。
        result.FullTranscript.ShouldNotBeNull();
        result.FullTranscript!.ShouldContain("Let me check the logs first.");
    }

    [Fact]
    public async Task Run_NormalizesToolTitleToStableIdentifier()
    {
        var transport = new FakeCliAgentTransport([], onWrite: Server(
        [
            """{"jsonrpc":"2.0","method":"session/update","params":{"update":{"sessionUpdate":"tool_call","toolCallId":"t1","title":"Read file: /a/b.go","rawInput":{"path":"/a/b.go"}}}}"""
        ]));

        var events = await DrainAsync(Adapter(), transport, Context());

        events.ShouldContain(e => e.Type == CliAgentEventType.ToolUse && e.Tool == "read_file");
    }

    [Fact]
    public async Task Run_DefersToolUseUntilArgumentsAreComplete()
    {
        // 有的运行时逐 token 流式补齐入参。立刻外发会让 UI 看到 `{"comma` 这样的半截命令。
        var transport = new FakeCliAgentTransport([], onWrite: Server(
        [
            """{"jsonrpc":"2.0","method":"session/update","params":{"update":{"sessionUpdate":"tool_call","toolCallId":"t1","title":"Run command"}}}""",
            """{"jsonrpc":"2.0","method":"session/update","params":{"update":{"sessionUpdate":"tool_call_update","toolCallId":"t1","status":"completed","rawInput":{"command":"ls -la"},"rawOutput":"files"}}}"""
        ]));

        var events = await DrainAsync(Adapter(), transport, Context());

        var toolUse = events.Single(e => e.Type == CliAgentEventType.ToolUse);
        toolUse.Input.ShouldNotBeNull();
        toolUse.Input!["command"].ShouldBe("ls -la");

        events.ShouldContain(e => e.Type == CliAgentEventType.ToolResult && e.Output == "files");
    }

    [Fact]
    public async Task Run_AutoApprovesPermissionRequestBySelectingAnOfferedOption()
    {
        // 必须从 agent 给的 options 里挑。回一个它从未提供过的 id 等同于拒绝 ——
        // 某些实现会因此静默阻止每一次文件写入。
        var served = false;
        var transport = new FakeCliAgentTransport([], onWrite: line =>
        {
            var responses = Server()(line).ToList();

            using var document = JsonDocument.Parse(line);
            if (!served && document.RootElement.TryGetProperty("method", out var m)
                && m.GetString() == "initialize")
            {
                served = true;
                responses.Add("""
                    {"jsonrpc":"2.0","id":"perm-1","method":"session/request_permission","params":{"options":[{"optionId":"allow_once","kind":"allow_once"},{"optionId":"deny","kind":"reject_once"}]}}
                    """.Trim());
            }

            return responses;
        });

        await DrainAsync(Adapter(), transport, Context());

        var reply = transport.Written.FirstOrDefault(w => w.Contains("\"outcome\""));
        reply.ShouldNotBeNull();
        reply!.ShouldContain("\"optionId\":\"allow_once\"");
    }

    [Fact]
    public async Task Run_WhenNoSafeOptionIsOffered_RepliesWithAProtocolErrorNotCancelled()
    {
        // 回 "cancelled" 表示整个 turn 被取消 —— 那会让任务直接中止，而不只是拒绝这一个动作。
        var served = false;
        var transport = new FakeCliAgentTransport([], onWrite: line =>
        {
            var responses = Server()(line).ToList();

            using var document = JsonDocument.Parse(line);
            if (!served && document.RootElement.TryGetProperty("method", out var m)
                && m.GetString() == "initialize")
            {
                served = true;
                responses.Add("""
                    {"jsonrpc":"2.0","id":"perm-2","method":"session/request_permission","params":{"options":[{"optionId":"never","kind":"reject_always"}]}}
                    """.Trim());
            }

            return responses;
        });

        await DrainAsync(Adapter(), transport, Context());

        var reply = transport.Written.FirstOrDefault(w => w.Contains("perm-2") && w.Contains("error"));
        reply.ShouldNotBeNull();
        reply!.ShouldNotContain("cancelled");
    }

    [Fact]
    public async Task Run_WhenPromptFailsWithSessionNotFoundOnAResume_MarksResumeRejected()
    {
        // 多数运行时在 session/resume 时把请求的 id 原样回来，即使会话已经没了；
        // 真正暴露是在 prompt 阶段。所以判定点在这里。
        var transport = new FakeCliAgentTransport([], onWrite: Server(failPromptWithSessionNotFound: true));

        var adapter = Adapter();
        await DrainAsync(adapter, transport, Context(resume: "acp-1"));

        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 1 });
        result.ResumeRejected.ShouldBeTrue();
        result.FailureReason.ShouldBe(CliRunFailureReason.ResumeRejected);
        result.SessionId.ShouldBeNull();
    }

    [Fact]
    public async Task Run_WhenModelSwitchFails_FailsTheRunInsteadOfSilentlyUsingTheDefault()
    {
        // 静默回落到默认模型会让用户以为自己选的模型生效了，而任务跑在别的模型上 ——
        // 成本与质量都对不上，且完全没有痕迹。
        var transport = new FakeCliAgentTransport([], onWrite: Server(failSetModel: true));

        var adapter = Adapter();
        await DrainAsync(adapter, transport, Context(model: "kimi-k2"));

        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 1 });
        result.Status.ShouldBe(CliRunStatus.Failed);
        result.Error.ShouldNotBeNull();
        result.Error!.ShouldContain("kimi-k2");
    }

    [Fact]
    public async Task GetResult_PromotesEndTurnToFailureWhenStderrShowsATerminalProviderError()
    {
        // 多个 ACP 运行时在上游 HTTP 失败时仍报 stopReason=end_turn。
        // 不提升的话用户看到的是一句空回复，而不是「token 过期」。
        var transport = new FakeCliAgentTransport([], onWrite: Server());

        var adapter = Adapter();
        await DrainAsync(adapter, transport, Context());

        var result = adapter.GetResult(new CliSessionOutcome
        {
            ExitCode = 0,
            StderrTail = "API call failed after 5 retries: 401 Unauthorized"
        });

        result.Status.ShouldBe(CliRunStatus.Failed);
        result.FailureReason.ShouldBe(CliRunFailureReason.AuthenticationFailed);
    }
}
