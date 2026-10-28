namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// stream-json 适配器的事件归一化与终态判定。
/// </summary>
public class StreamJsonAdapterTests
{
    private static CliAgentLaunchContext Context(string? resumeSessionId = null) => new()
    {
        Provider = CliBuiltInProviders.All["claude"],
        ExecutablePath = "/nonexistent/claude",
        Prompt = "hello",
        WorkingDirectory = "/tmp/does-not-need-to-exist",
        ResumeSessionId = resumeSessionId
    };

    private static StreamJsonAdapter Adapter() => new(NullLogger<StreamJsonAdapter>.Instance);

    private static async Task<List<CliAgentEvent>> DrainAsync(
        StreamJsonAdapter adapter, FakeCliAgentTransport transport, CliAgentLaunchContext context)
    {
        var events = new List<CliAgentEvent>();
        await foreach (var evt in adapter.RunAsync(transport, context, CancellationToken.None))
        {
            events.Add(evt);
        }

        return events;
    }

    [Fact]
    public void BuildProcess_NeverPutsThePromptOnTheCommandLine()
    {
        // 提示词进命令行有两个后果：出现在同机其他用户可见的进程列表里，
        // 以及长提示撞上命令行长度上限。它必须走 stdin。
        var spec = Adapter().BuildProcess(Context() with { Prompt = "secret business context" });

        spec.Arguments.ShouldNotContain(a => a.Contains("secret business context"));
        spec.Arguments.ShouldContain("--output-format");
        spec.Arguments.ShouldContain("stream-json");
    }

    [Fact]
    public void BuildProcess_DropsProtocolCriticalCustomArgsAndTheirValues()
    {
        var spec = Adapter().BuildProcess(Context() with
        {
            CustomArgs = ["--output-format", "text", "--allowed-tools", "Bash"]
        });

        // 带值参数必须连值一起吞掉，否则 "text" 会作为裸位置参数漏给 CLI。
        spec.Arguments.ShouldNotContain("text");
        spec.Arguments.ShouldContain("--allowed-tools");
        spec.Arguments.ShouldContain("Bash");
    }

    [Fact]
    public void BuildProcess_StripsShellQuotesFromCustomArgs()
    {
        var spec = Adapter().BuildProcess(Context() with { CustomArgs = ["--deny-tool='write'"] });

        spec.Arguments.ShouldContain("--deny-tool=write");
    }

    /// <summary>上下文的缺省（直接构造的调用方、本机宿主）就是此前的行为：全权、有受管 MCP 时只用它。</summary>
    [Fact]
    public void BuildProcess_ByDefault_KeepsBypassPermissionsAndAStrictManagedMcpConfig()
    {
        var spec = Adapter().BuildProcess(Context() with { McpConfigPath = "/ws/.tnzi/mcp.json" });

        ValueOf(spec.Arguments, "--permission-mode").ShouldBe("bypassPermissions");
        spec.Arguments.ShouldContain("--strict-mcp-config");
    }

    /// <summary>四档按实测映射：只读与先问我都是 default（没有批准通道时拒绝要权限的操作），不是只出计划的 plan。</summary>
    [Theory]
    [InlineData(CliPermissionMode.Full, "bypassPermissions")]
    [InlineData(CliPermissionMode.EditWorkspace, "acceptEdits")]
    [InlineData(CliPermissionMode.ReadOnly, "default")]
    [InlineData(CliPermissionMode.Ask, "default")]
    public void BuildProcess_PermissionMode_MapsToTheCliMode(CliPermissionMode mode, string expected)
    {
        var spec = Adapter().BuildProcess(Context() with { PermissionMode = mode });

        ValueOf(spec.Arguments, "--permission-mode").ShouldBe(expected);
        spec.Arguments.Count(a => a == "--permission-mode").ShouldBe(1);
    }

    /// <summary>
    /// 非全权档时部署级参数里的 <c>--dangerously-skip-permissions</c> 被丢掉：它会把档位整个绕过去。
    /// </summary>
    [Fact]
    public void BuildProcess_ARestrictedMode_DropsTheSwitchThatWouldBypassIt()
    {
        var spec = Adapter().BuildProcess(Context() with
        {
            PermissionMode = CliPermissionMode.EditWorkspace,
            ExtraArgs = ["--dangerously-skip-permissions"]
        });

        spec.Arguments.ShouldNotContain("--dangerously-skip-permissions");
    }

    /// <summary>
    /// 不 strict 时受管配置与 CLI 自己的 MCP 合并：不加 <c>--strict-mcp-config</c>，部署级参数里的也丢掉。
    /// </summary>
    [Fact]
    public void BuildProcess_NotStrict_MergesWithTheUsersOwnMcpServers()
    {
        var spec = Adapter().BuildProcess(Context() with
        {
            McpConfigPath = "/ws/.tnzi/mcp.json",
            StrictMcpConfig = false,
            ExtraArgs = ["--strict-mcp-config"]
        });

        ValueOf(spec.Arguments, "--mcp-config").ShouldBe("/ws/.tnzi/mcp.json");
        spec.Arguments.ShouldNotContain("--strict-mcp-config");
    }

    /// <summary>
    /// 档案接管的开关只出现一次、取档案的值：部署级参数里的 <c>--tools</c>（此前限定所有运行的那种写法）这一轮被丢掉，
    /// 档案没接管的照常保留。
    /// </summary>
    [Fact]
    public void BuildProcess_OwnedArgs_WinOverTheDeploymentsExtraArgs()
    {
        var spec = Adapter().BuildProcess(Context() with
        {
            ManagedArgs = ["--tools", "WebSearch", "--add-dir", "/opt/platform"],
            OwnedArgs = new Dictionary<string, BlockedArgMode> { ["--tools"] = BlockedArgMode.WithValue },
            ExtraArgs = ["--tools", "Bash,Read", "--add-dir", "/extra"],
            CustomArgs = ["--tools=Edit"]
        });

        spec.Arguments.Count(a => a == "--tools" || a.StartsWith("--tools=", StringComparison.Ordinal)).ShouldBe(1);
        ValueOf(spec.Arguments, "--tools").ShouldBe("WebSearch");
        spec.Arguments.ShouldNotContain("Bash,Read");
        spec.Arguments.ShouldContain("/extra", "a switch the profile does not own is left alone");
    }

    /// <summary>
    /// 非全权档下问到适配器这里的许可请求就是要人批准的：眼下没有人可问，拒绝（附说明），不自动批准。
    /// </summary>
    [Fact]
    public async Task Run_ARestrictedMode_DeniesPermissionRequests()
    {
        var transport = new FakeCliAgentTransport(
            [
                """{"type":"control_request","request_id":"req-9","request":{"subtype":"can_use_tool","tool_name":"Bash","input":{"command":"python3 -c 'open(\"x\",\"w\")'"}}}""",
                """{"type":"result","subtype":"success","session_id":"s","result":"done","num_turns":1}"""
            ],
            onWrite: _ => []);

        await DrainAsync(Adapter(), transport, Context() with { PermissionMode = CliPermissionMode.EditWorkspace });

        var reply = transport.Written.Single(w => w.Contains("req-9"));
        reply.ShouldContain("\"behavior\":\"deny\"");
        reply.ShouldNotContain("\"allow\"");
    }

    private static string? ValueOf(IReadOnlyList<string> arguments, string flag)
    {
        var index = arguments.ToList().IndexOf(flag);
        return index >= 0 && index + 1 < arguments.Count ? arguments[index + 1] : null;
    }

    [Fact]
    public async Task Run_TreatsResultEventAsTheOnlyTerminationSignal()
    {
        // 关键实测结论：因为 stdin 保持打开，CLI 不会自行退出，stdout 也不会关闭。
        // 等 EOF 的写法会让每个 turn 挂满超时窗口。这里的假 transport 刻意<b>不</b>结束流，
        // 只要适配器仍然返回，就证明它以 result 为终止信号。
        var transport = new FakeCliAgentTransport(
            [
                """{"type":"system","session_id":"sess-1"}""",
                """{"type":"assistant","message":{"role":"assistant","model":"m","content":[{"type":"text","text":"OK"}]}}""",
                """{"type":"result","subtype":"success","session_id":"sess-1","result":"OK","num_turns":1}"""
            ],
            onWrite: _ => []);

        var adapter = Adapter();
        var events = await DrainAsync(adapter, transport, Context());

        events.ShouldContain(e => e.Type == CliAgentEventType.Text && e.Content == "OK");

        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 0 });
        result.Status.ShouldBe(CliRunStatus.Completed);
        result.Output.ShouldBe("OK");
        result.SessionId.ShouldBe("sess-1");

        // 拿到终态后主动关 stdin，让 CLI 自行收尾退出。
        transport.InputClosed.ShouldBeTrue();
    }

    [Fact]
    public async Task Run_EmitsSessionIdEarlyOnTheSystemEvent()
    {
        // 进程中途崩溃时，等终态才拿 sessionId 就已经晚了 —— 续接指针会丢。
        var transport = new FakeCliAgentTransport(
            [
                """{"type":"system","session_id":"sess-early"}""",
                """{"type":"result","subtype":"success","session_id":"sess-early","result":"done"}"""
            ],
            onWrite: _ => []);

        var events = await DrainAsync(Adapter(), transport, Context());

        events.ShouldContain(e => e.Type == CliAgentEventType.Status && e.SessionId == "sess-early");
    }

    [Fact]
    public async Task Run_NormalizesRateLimitEventToStatusRatherThanLog()
    {
        // 实测存在的带外事件：配额窗口播报。它既不是内容也不是错误 ——
        // 归一化为 Status 才能让上层转成「接近限额」的用户提示。
        var transport = new FakeCliAgentTransport(
            [
                """{"type":"rate_limit_event","status":"approaching"}""",
                """{"type":"result","subtype":"success","result":"done"}"""
            ],
            onWrite: _ => []);

        var events = await DrainAsync(Adapter(), transport, Context());

        events.ShouldContain(e => e.Type == CliAgentEventType.Status && e.Status == "rate_limit");
    }

    [Fact]
    public async Task Run_ToolProgressHeartbeats_AreRecognisedAndProduceNoEvents()
    {
        // claude 在工具执行期间约每 30 秒发一次 tool_progress（MCP 工具带 heartbeat，Bash 带 task_id）。
        // 它不是未知事件：此前每一次都落成一条 "unhandled event type" 的 Log 行
        var transport = new FakeCliAgentTransport(
            [
                """{"type":"assistant","message":{"role":"assistant","model":"m","content":[{"type":"tool_use","id":"toolu_1","name":"mcp__tnzi__run_command","input":{"command":"scan"}}]}}""",
                """{"type":"tool_progress","tool_use_id":"toolu_1","tool_name":"mcp__tnzi__run_command","parent_tool_use_id":null,"elapsed_time_seconds":30,"heartbeat":true,"session_id":"s","uuid":"u1"}""",
                """{"type":"tool_progress","tool_use_id":"toolu_1","tool_name":"mcp__tnzi__run_command","parent_tool_use_id":null,"elapsed_time_seconds":60,"heartbeat":true,"session_id":"s","uuid":"u2"}""",
                """{"type":"tool_progress","tool_use_id":"toolu_2","tool_name":"Bash","parent_tool_use_id":null,"elapsed_time_seconds":30,"task_id":"task-1","session_id":"s","uuid":"u3"}""",
                """{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"toolu_1","content":"done"}]}}""",
                """{"type":"result","subtype":"success","result":"scanned"}"""
            ],
            onWrite: _ => []);

        var adapter = Adapter();
        var events = await DrainAsync(adapter, transport, Context());

        events.Select(e => e.Type).ShouldBe([CliAgentEventType.ToolUse, CliAgentEventType.ToolResult]);
        adapter.GetResult(new CliSessionOutcome { ExitCode = 0 }).Status.ShouldBe(CliRunStatus.Completed);
    }

    [Fact]
    public async Task Run_DowngradesUnknownEventTypeToLogInsteadOfThrowing()
    {
        // CLI 会随版本新增事件类型。把未知形状当错误处理 = 每次上游升级都是一次故障。
        var transport = new FakeCliAgentTransport(
            [
                """{"type":"some_future_event","payload":{"x":1}}""",
                """not even json""",
                """{"type":"result","subtype":"success","result":"done"}"""
            ],
            onWrite: _ => []);

        var adapter = Adapter();
        var events = await DrainAsync(adapter, transport, Context());

        events.ShouldContain(e => e.Type == CliAgentEventType.Log && e.Content!.Contains("some_future_event"));
        adapter.GetResult(new CliSessionOutcome { ExitCode = 0 }).Status.ShouldBe(CliRunStatus.Completed);
    }

    [Fact]
    public async Task Run_AnswersControlRequestOnTheSameStdin()
    {
        // 不应答的后果不是丢一条消息，而是子进程阻塞到它自己的内部超时，整个任务卡死。
        var transport = new FakeCliAgentTransport(
            [
                """{"type":"control_request","request_id":"req-1","request":{"subtype":"can_use_tool","tool_name":"Bash","input":{"command":"ls"}}}""",
                """{"type":"result","subtype":"success","result":"done"}"""
            ],
            onWrite: _ => []);

        await DrainAsync(Adapter(), transport, Context());

        transport.Written.ShouldContain(w => w.Contains("control_response") && w.Contains("\"behavior\":\"allow\""));
    }

    [Fact]
    public async Task Run_ApprovedToolInput_IsEchoedUnchanged_IncludingNestedValues()
    {
        // updatedInput 会替换工具的入参：嵌套的数组 / 对象若被压成字符串，MultiEdit、TodoWrite 这类工具拿到的就是坏参数
        const string input = """{"file_path":"a.txt","edits":[{"old_string":"x","new_string":"y"}],"options":{"dry":false,"n":2}}""";
        var transport = new FakeCliAgentTransport(
            [
                $$$"""{"type":"control_request","request_id":"req-2","request":{"subtype":"can_use_tool","tool_name":"MultiEdit","input":{{{input}}}}}""",
                """{"type":"result","subtype":"success","result":"done"}"""
            ],
            onWrite: _ => []);

        await DrainAsync(Adapter(), transport, Context());

        var written = transport.Written.Single(w => w.Contains("control_response"));
        using var doc = JsonDocument.Parse(written);
        var response = doc.RootElement.GetProperty("response");
        response.GetProperty("subtype").GetString().ShouldBe("success");
        response.GetProperty("request_id").GetString().ShouldBe("req-2");
        var updatedInput = response.GetProperty("response").GetProperty("updatedInput");
        updatedInput.GetProperty("edits").ValueKind.ShouldBe(JsonValueKind.Array);
        updatedInput.GetProperty("options").ValueKind.ShouldBe(JsonValueKind.Object);
        JsonNode.DeepEquals(JsonNode.Parse(updatedInput.GetRawText()), JsonNode.Parse(input)).ShouldBeTrue();
    }

    [Fact]
    public async Task Run_ControlRequestOfAnotherSubtype_IsAnsweredWithAnError_NotAnApproval()
    {
        // 只有 can_use_tool 是许可请求；别的子类型收到一份 allow 形状的应答等于答非所问。仍然要应答，否则子进程卡到超时
        var transport = new FakeCliAgentTransport(
            [
                """{"type":"control_request","request_id":"req-3","request":{"subtype":"hook_callback","callback_id":"cb-1","input":{}}}""",
                """{"type":"result","subtype":"success","result":"done"}"""
            ],
            onWrite: _ => []);

        await DrainAsync(Adapter(), transport, Context());

        var written = transport.Written.Single(w => w.Contains("control_response"));
        using var doc = JsonDocument.Parse(written);
        var response = doc.RootElement.GetProperty("response");
        response.GetProperty("subtype").GetString().ShouldBe("error");
        response.GetProperty("request_id").GetString().ShouldBe("req-3");
        response.GetProperty("error").GetString().ShouldNotBeNullOrWhiteSpace();
        written.ShouldNotContain("\"behavior\"");
    }

    [Fact]
    public async Task GetResult_WithoutResultEvent_FailsInsteadOfReportingSuccess()
    {
        // 进程干净退出不等于协议跑完。没有 result 就没有终态。
        var transport = new FakeCliAgentTransport(
            ["""{"type":"assistant","message":{"role":"assistant","model":"m","content":[{"type":"text","text":"partial"}]}}"""]);

        var adapter = Adapter();
        await DrainAsync(adapter, transport, Context());

        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 0 });
        result.Status.ShouldBe(CliRunStatus.Failed);
        result.FailureReason.ShouldBe(CliRunFailureReason.ProcessCrashed);
        // 失败时不给交付物：一段被截断的记录被当成"最终答案"回给用户，比一条明确的失败更糟。
        result.Output.ShouldBeNull();
    }

    [Fact]
    public async Task GetResult_WhenAssistantTurnInvokedATool_DoesNotUseItsNarrationAsOutput()
    {
        // 「我先看一下日志」不是交付物。
        var transport = new FakeCliAgentTransport(
            [
                """{"type":"assistant","message":{"role":"assistant","model":"m","content":[{"type":"text","text":"Let me check the logs"},{"type":"tool_use","id":"t1","name":"Bash","input":{"command":"cat log"}}]}}""",
                """{"type":"result","subtype":"success","result":""}"""
            ],
            onWrite: _ => []);

        var adapter = Adapter();
        await DrainAsync(adapter, transport, Context());

        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 0 });
        result.Output.ShouldNotBeNull();
        result.Output!.ShouldNotContain("Let me check the logs");
    }

    [Fact]
    public async Task GetResult_DetectsResumeRejectionFromStderrPhrase()
    {
        // 实测：result 事件的 subtype 是通用的 error_during_execution，不能用来判断；
        // 真正的区分信号在 stderr + 非零退出码。
        var transport = new FakeCliAgentTransport(
            ["""{"type":"result","subtype":"error_during_execution","is_error":true,"num_turns":0,"total_cost_usd":0,"session_id":"gone"}"""],
            stderrTail: "No conversation found with session ID: gone",
            onWrite: _ => []);

        var adapter = Adapter();
        await DrainAsync(adapter, transport, Context("gone"));

        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 1, StderrTail = "No conversation found with session ID: gone" });
        result.ResumeRejected.ShouldBeTrue();
        result.FailureReason.ShouldBe(CliRunFailureReason.ResumeRejected);
        // 已知被拒的会话指针不能保存 —— 下一轮拿它续接必然再失败一次。
        result.SessionId.ShouldBeNull();
    }

    /// <summary>
    /// 短语表与「非零退出 + 0 轮 + 0 成本 + stderr」的佐证是对内置 provider 实测的。自定义 provider
    /// 一律不声明能力位：它启动时因认证失败退出，佐证照样成立，判成被拒就会清掉仍然有效的会话指针、开新会话重跑。
    /// </summary>
    [Theory]
    [InlineData("Error: invalid API key")]
    [InlineData("No conversation found with session ID: gone")]
    public async Task GetResult_ForAProviderThatCannotTellARejection_NeverClaimsTheResumeWasRejected(string stderr)
    {
        var transport = new FakeCliAgentTransport(
            ["""{"type":"result","subtype":"error_during_execution","is_error":true,"num_turns":0,"total_cost_usd":0,"session_id":"still-good"}"""],
            stderrTail: stderr,
            onWrite: _ => []);
        var custom = new CliProviderDescriptor
        {
            Key = "my-cli",
            DisplayName = "My CLI",
            Protocol = CliAgentProtocol.StreamJson,
            DefaultExecutable = "my-cli",
            ResumeRejectionDetectable = false
        };

        var adapter = Adapter();
        await DrainAsync(adapter, transport, Context("still-good") with { Provider = custom });

        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 1, StderrTail = stderr });
        result.Status.ShouldBe(CliRunStatus.Failed);
        result.ResumeRejected.ShouldBeFalse();
        result.FailureReason.ShouldNotBe(CliRunFailureReason.ResumeRejected);
        result.SessionId.ShouldBe("still-good", "the pointer is kept for the next turn");
    }

    [Fact]
    public async Task GetResult_ForABuiltInProvider_StillUsesTheCorroboratingSignals()
    {
        // 对照：内置 provider 声明了能力位，辅助佐证照常生效
        var transport = new FakeCliAgentTransport(
            ["""{"type":"result","subtype":"error_during_execution","is_error":true,"num_turns":0,"total_cost_usd":0}"""],
            stderrTail: "some startup failure",
            onWrite: _ => []);

        var adapter = Adapter();
        await DrainAsync(adapter, transport, Context("gone"));

        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 1, StderrTail = "some startup failure" });
        result.ResumeRejected.ShouldBeTrue();
    }

    [Fact]
    public async Task GetResult_OnCancellation_DoesNotClaimTheResumeWasRejected()
    {
        // 取消是框架自己造成的。把它算成 resume 被拒，会白白丢掉一个仍然有效的会话指针。
        // 进程被中止后 stdout 随之关闭，所以这里用「已结束的流」建构。
        var transport = new FakeCliAgentTransport([]);

        var adapter = Adapter();
        await DrainAsync(adapter, transport, Context("still-good"));

        var result = adapter.GetResult(new CliSessionOutcome { Cancelled = true, ExitCode = 1 });
        result.Status.ShouldBe(CliRunStatus.Cancelled);
        result.ResumeRejected.ShouldBeFalse();
    }

    [Fact]
    public async Task GetResult_OnWatchdogTimeout_DoesNotClaimTheResumeWasRejected()
    {
        var transport = new FakeCliAgentTransport([]);

        var adapter = Adapter();
        await DrainAsync(adapter, transport, Context("still-good"));

        var result = adapter.GetResult(new CliSessionOutcome
        {
            WatchdogFailure = CliRunFailureReason.IdleTimeout,
            ExitCode = 1,
            StderrTail = "some noise"
        });

        result.Status.ShouldBe(CliRunStatus.TimedOut);
        result.ResumeRejected.ShouldBeFalse();
    }

    [Fact]
    public async Task GetResult_PrefersResultModelUsageOverPerFrameAccumulation()
    {
        // result 帧的 modelUsage 覆盖这次进程里全部的模型调用（工具内部的、子 agent 的），两者相加会双计。
        // 它是会话累计值（续接时带着之前的轮次），所以标成 UsageIsSessionTotal，折算交给执行器。
        var transport = new FakeCliAgentTransport(
            [
                """{"type":"assistant","message":{"role":"assistant","model":"m","usage":{"input_tokens":5,"output_tokens":3},"content":[{"type":"text","text":"hi"}]}}""",
                """{"type":"result","subtype":"success","result":"hi","modelUsage":{"m":{"inputTokens":5,"outputTokens":3,"cacheReadInputTokens":100,"cacheCreationInputTokens":50}}}"""
            ],
            onWrite: _ => []);

        var adapter = Adapter();
        await DrainAsync(adapter, transport, Context());

        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 0 });
        result.UsageIsSessionTotal.ShouldBeTrue();
        var usage = result.Usage;
        usage["m"].InputTokens.ShouldBe(5);
        usage["m"].OutputTokens.ShouldBe(3);
        usage["m"].CacheReadTokens.ShouldBe(100);
    }
}
