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
    public async Task Run_WithAReasoningEffortOnTheBinding_SaysItIsNotApplied()
    {
        // 管理端对所有运行时都提供推理强度，ACP 一路没有地方投递它：照常运行，但不能静默丢掉。
        var logger = new Mock<ILogger<AcpAdapter>>();
        var transport = new FakeCliAgentTransport([], onWrite: Server());

        await DrainAsync(new AcpAdapter(logger.Object), transport, Context() with { ThinkingLevel = "high" });

        logger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => state != null && state.ToString()!.Contains("reasoning effort 'high' is not applied")),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        transport.Written.ShouldNotContain(w => w.Contains("high"), "nothing carries it to the runtime");
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

    /// <summary>
    /// 权限档位落在应答上：按 <c>toolCall.kind</c> 放行或单次拒绝（不回 cancelled，那会中止整个 turn）。
    /// </summary>
    /// <remarks>与 stream-json 的实测映射对齐：改工作区放行编辑、拒绝执行命令；只读与先问我只放行读与搜索；没有 kind 的一律不放行。</remarks>
    [Theory]
    [InlineData(CliPermissionMode.Full, "execute", "allow_once")]
    [InlineData(CliPermissionMode.EditWorkspace, "edit", "allow_once")]
    [InlineData(CliPermissionMode.EditWorkspace, "execute", "deny")]
    [InlineData(CliPermissionMode.EditWorkspace, "fetch", "deny")]
    [InlineData(CliPermissionMode.ReadOnly, "read", "allow_once")]
    [InlineData(CliPermissionMode.ReadOnly, "edit", "deny")]
    [InlineData(CliPermissionMode.Ask, "delete", "deny")]
    [InlineData(CliPermissionMode.ReadOnly, null, "deny")]
    public async Task Run_PermissionMode_DecidesWhichToolRequestsAreApproved(CliPermissionMode mode, string? kind, string expectedOption)
    {
        var served = false;
        var toolCall = kind is null ? """{"toolCallId":"t1"}""" : $$"""{"toolCallId":"t1","kind":"{{kind}}"}""";
        var transport = new FakeCliAgentTransport([], onWrite: line =>
        {
            var responses = Server()(line).ToList();

            using var document = JsonDocument.Parse(line);
            if (!served && document.RootElement.TryGetProperty("method", out var m)
                && m.GetString() == "initialize")
            {
                served = true;
                responses.Add($$$"""
                    {"jsonrpc":"2.0","id":"perm-m","method":"session/request_permission","params":{"toolCall":{{{toolCall}}},"options":[{"optionId":"allow_once","kind":"allow_once"},{"optionId":"deny","kind":"reject_once"}]}}
                    """.Trim());
            }

            return responses;
        });

        await DrainAsync(Adapter(), transport, Context() with { PermissionMode = mode });

        var reply = transport.Written.FirstOrDefault(w => w.Contains("perm-m") && w.Contains("\"outcome\""));
        reply.ShouldNotBeNull();
        reply!.ShouldContain($"\"optionId\":\"{expectedOption}\"");
    }

    /// <summary>
    /// 非全权档不带「一律自动批准」的启动开关：带着它 CLI 从不请求权限，档位落不到应答上。
    /// </summary>
    [Theory]
    [InlineData("qoder", new[] { "--yolo", "--acp" }, new[] { "--acp" })]
    [InlineData("grok", new[] { "agent", "--always-approve", "stdio" }, new[] { "agent", "stdio" })]
    public void BuildProcess_ARestrictedMode_LeavesOutTheAutoApprovalSwitch(string providerKey, string[] full, string[] restricted)
    {
        var context = Context() with { Provider = CliBuiltInProviders.All[providerKey] };

        Adapter().BuildProcess(context).Arguments.ShouldBe(full);
        Adapter().BuildProcess(context with { PermissionMode = CliPermissionMode.ReadOnly }).Arguments.ShouldBe(restricted);
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
    [Fact]
    public async Task Run_CancelledWhileThePromptIsPending_HandsTheCancellationBackToTheExecutor()
    {
        // 生命周期任务吞掉取消、正常收尾时，事件流正常结束：执行器分不出「取消」与「跑完」，
        // 用户取消与宿主停机都被记成 Completed，半句话成了交付物。取消必须以 OCE 交回去（与 stream-json 同一契约）。
        var promptSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = Server();
        var transport = new FakeCliAgentTransport([], onWrite: line =>
        {
            if (line.Contains("\"session/prompt\"", StringComparison.Ordinal))
            {
                promptSent.TrySetResult();
                return []; // 运行时还在干活，迟迟不答
            }

            return server(line);
        });

        using var cts = new CancellationTokenSource();
        var adapter = Adapter();
        var run = Task.Run(async () =>
        {
            await foreach (var _ in adapter.RunAsync(transport, Context(), cts.Token))
            {
            }
        });

        await promptSent.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Run_CancelledAfterThePromptAnswered_CompletesWithTheAnswer()
    {
        // 回合已经答完才到的取消不算取消：记成取消（或打回队列重跑）会丢掉一份已经付过钱的完整答案
        var answered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = Server(
        [
            """{"jsonrpc":"2.0","method":"session/update","params":{"update":{"sessionUpdate":"agent_message_chunk","content":{"type":"text","text":"Done."}}}}"""
        ]);
        var transport = new FakeCliAgentTransport([], onWrite: line =>
        {
            var responses = server(line).ToList();
            if (line.Contains("\"session/prompt\"", StringComparison.Ordinal))
            {
                answered.TrySetResult();
            }

            return responses;
        });

        using var cts = new CancellationTokenSource();
        var adapter = Adapter();
        var run = Task.Run(async () =>
        {
            await foreach (var _ in adapter.RunAsync(transport, Context(), cts.Token))
            {
            }
        });

        await answered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(500); // 响应已被读循环处理，适配器停在收尾窗口里
        await cts.CancelAsync();

        await run.WaitAsync(TimeSpan.FromSeconds(10));
        var result = adapter.GetResult(new CliSessionOutcome { ExitCode = 0 });
        result.Status.ShouldBe(CliRunStatus.Completed);
        result.Output.ShouldBe("Done.");
    }
    [Fact]
    public async Task Run_SessionNew_CarriesTheWriteBackServerInTheAcpShape()
    {
        // 回写通道（http + Authorization）对声明支持 http 的运行时原样送进 session/new：
        // 请求头必须是 {name, value} 数组，对象形的请求头会让按 schema 校验的运行时拒掉整个会话
        var config = new CliMcpConfigComposer(NullLogger<CliMcpConfigComposer>.Instance).Compose(
            null, "run-token", new CliWriteBackOptions { Enabled = true, McpEndpoint = "https://host/mcp" }, LocalHosts.Create());
        {
            var server = Server();
            var transport = new FakeCliAgentTransport([], onWrite: line =>
            {
                if (line.Contains("\"initialize\"", StringComparison.Ordinal))
                {
                    var id = JsonDocument.Parse(line).RootElement.GetProperty("id").GetRawText();
                    return [Ok(id, """{"agentCapabilities":{"mcpCapabilities":{"http":true}}}""")];
                }

                return server(line);
            });

            // 路径指向执行目标上的文件（这里根本不存在）：适配器读的是随上下文来的内容，不碰文件系统
            await DrainAsync(Adapter(), transport, Context() with { McpConfigPath = "/home/agent/ws/.tnzi/mcp.json", McpConfigJson = config });

            var sessionNew = transport.Written
                .Select(w => JsonDocument.Parse(w).RootElement)
                .Single(e => e.TryGetProperty("method", out var m) && m.GetString() == "session/new");
            var mcp = sessionNew.GetProperty("params").GetProperty("mcpServers").EnumerateArray().ShouldHaveSingleItem();
            mcp.GetProperty("type").GetString().ShouldBe("http");
            var header = mcp.GetProperty("headers").EnumerateArray().ShouldHaveSingleItem();
            header.GetProperty("name").GetString().ShouldBe("Authorization");
            header.GetProperty("value").GetString().ShouldBe("Bearer run-token");
        }
    }

    [Fact]
    public async Task Run_SessionNew_CarriesAHostProvidedStdioWriteBackEntry()
    {
        // 连不到平台地址的远程目标把回写通道写成驻留代理的中继命令：ACP 运行时要收到 stdio 形状（args / env 恒在），
        // 且不受 mcpCapabilities 过滤（stdio 是 ACP 的基础能力）
        var remote = new TestCliHost((_, _) => throw new InvalidOperationException("not started"), writeBackEntry: channel => new JsonObject
        {
            ["type"] = "stdio",
            ["command"] = "/opt/agent/bin/agent",
            ["args"] = new JsonArray("mcp-relay"),
            ["env"] = new JsonObject { ["RELAY_TOKEN"] = channel.Token }
        });
        var config = new CliMcpConfigComposer(NullLogger<CliMcpConfigComposer>.Instance).Compose(
            null, "run-token", new CliWriteBackOptions { Enabled = true, McpEndpoint = "https://host/mcp" }, remote);
        var transport = new FakeCliAgentTransport([], onWrite: Server());

        await DrainAsync(Adapter(), transport, Context() with { McpConfigJson = config });

        var sessionNew = transport.Written
            .Select(w => JsonDocument.Parse(w).RootElement)
            .Single(e => e.TryGetProperty("method", out var m) && m.GetString() == "session/new");
        var mcp = sessionNew.GetProperty("params").GetProperty("mcpServers").EnumerateArray().ShouldHaveSingleItem();
        mcp.TryGetProperty("url", out _).ShouldBeFalse();
        mcp.GetProperty("command").GetString().ShouldBe("/opt/agent/bin/agent");
        mcp.GetProperty("args").EnumerateArray().Select(a => a.GetString()).ShouldBe(["mcp-relay"]);
        var env = mcp.GetProperty("env").EnumerateArray().ShouldHaveSingleItem();
        env.GetProperty("name").GetString().ShouldBe("RELAY_TOKEN");
        env.GetProperty("value").GetString().ShouldBe("run-token");
    }
}
