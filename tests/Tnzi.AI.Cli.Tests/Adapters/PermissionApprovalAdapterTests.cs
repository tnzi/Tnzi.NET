namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 「先问我」档在有人值守时：两个适配器把权限请求交成 <see cref="CliAgentEventType.PermissionRequest"/> 事件、不当场应答，
/// 等 <see cref="ICliProtocolAdapter.AnswerPermissionAsync"/> 把使用方的回答写给 CLI。
/// </summary>
public class PermissionApprovalAdapterTests
{
    private const string Result = """{"type":"result","subtype":"success","session_id":"s","result":"done","num_turns":1}""";

    private static CliAgentLaunchContext StreamJsonContext(CliPermissionMode mode = CliPermissionMode.Ask, bool asks = true) => new()
    {
        Provider = CliBuiltInProviders.All["claude"],
        ExecutablePath = "/nonexistent/claude",
        Prompt = "hello",
        WorkingDirectory = "/tmp/does-not-need-to-exist",
        PermissionMode = mode,
        AsksForApproval = asks
    };

    private static CliAgentLaunchContext AcpContext(bool asks = true) => new()
    {
        Provider = CliBuiltInProviders.All["kimi"],
        ExecutablePath = "/nonexistent/kimi",
        Prompt = "hello",
        WorkingDirectory = "/tmp/does-not-need-to-exist",
        HandshakeTimeout = TimeSpan.FromSeconds(5),
        PermissionMode = CliPermissionMode.Ask,
        AsksForApproval = asks
    };

    private static string CanUseTool(string requestId, string extra = "")
        => $$$"""{"type":"control_request","request_id":"{{{requestId}}}","request":{"subtype":"can_use_tool","tool_name":"Bash","tool_use_id":"toolu_1","input":{"command":"rm -rf build","nested":{"a":[1,2]}},"description":"Remove the build directory","decision_reason":"\u001b[1mBash\u001b[0m needs approval"{{{extra}}}}}""";

    /// <summary>读完全部事件；遇到权限请求就按 <paramref name="answer"/> 回答（在迭代当中，像执行器的批准通道那样从外部调用）。</summary>
    private static async Task<List<CliAgentEvent>> RunAnsweringAsync(
        ICliProtocolAdapter adapter, ICliAgentTransport transport, CliAgentLaunchContext context,
        Func<CliAgentEvent, CliPermissionDecision?> answer)
    {
        var events = new List<CliAgentEvent>();
        await foreach (var evt in adapter.RunAsync(transport, context, CancellationToken.None))
        {
            events.Add(evt);
            if (evt.Type == CliAgentEventType.PermissionRequest && answer(evt) is { } decision)
            {
                (await adapter.AnswerPermissionAsync(evt.Permission!.ProtocolKey!, decision, CancellationToken.None)).ShouldBeTrue();
            }
        }

        return events;
    }

    // ── stream-json ─────────────────────────────────────────────────────────

    [Fact]
    public async Task StreamJson_AskWithSomeoneAttending_EmitsTheRequestAndAnswersNothingUntilAnswered()
    {
        var transport = new FakeCliAgentTransport([CanUseTool("req-1")], onWrite: line => line.Contains("control_response") ? [Result] : []);
        var adapter = new StreamJsonAdapter(NullLogger<StreamJsonAdapter>.Instance);
        var writesBeforeAnswer = -1;

        var events = await RunAnsweringAsync(adapter, transport, StreamJsonContext(), _ =>
        {
            writesBeforeAnswer = transport.Written.Count(w => w.Contains("control_response"));
            return new CliPermissionDecision(true, CliPermissionOptionKinds.AllowOnce, null);
        });

        writesBeforeAnswer.ShouldBe(0);
        var request = events.Single(e => e.Type == CliAgentEventType.PermissionRequest);
        request.Tool.ShouldBe("Bash");
        request.CallId.ShouldBe("toolu_1");
        request.Content.ShouldBe("Remove the build directory");
        request.Input!["command"].ShouldBe("rm -rf build");
        request.Permission!.ProtocolKey.ShouldBe("req-1");
        request.Permission.Reason.ShouldBe("Bash needs approval");
        request.Permission.Options.Select(o => o.Kind).ShouldBe([CliPermissionOptionKinds.AllowOnce, CliPermissionOptionKinds.RejectOnce]);

        using var reply = JsonDocument.Parse(transport.Written.Single(w => w.Contains("control_response")));
        var decision = reply.RootElement.GetProperty("response").GetProperty("response");
        reply.RootElement.GetProperty("response").GetProperty("request_id").GetString().ShouldBe("req-1");
        decision.GetProperty("behavior").GetString().ShouldBe("allow");
        decision.GetProperty("updatedInput").GetProperty("nested").GetProperty("a").GetArrayLength().ShouldBe(2);
        decision.TryGetProperty("updatedPermissions", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task StreamJson_Deny_WritesTheUsersMessage()
    {
        var transport = new FakeCliAgentTransport([CanUseTool("req-2")], onWrite: line => line.Contains("control_response") ? [Result] : []);

        await RunAnsweringAsync(new StreamJsonAdapter(NullLogger<StreamJsonAdapter>.Instance), transport, StreamJsonContext(),
            _ => new CliPermissionDecision(false, CliPermissionOptionKinds.RejectOnce, "Keep the build directory."));

        using var reply = JsonDocument.Parse(transport.Written.Single(w => w.Contains("control_response")));
        var decision = reply.RootElement.GetProperty("response").GetProperty("response");
        decision.GetProperty("behavior").GetString().ShouldBe("deny");
        decision.GetProperty("message").GetString().ShouldBe("Keep the build directory.");
    }

    /// <summary>「本会话总是允许」：带上 CLI 建议的规则，去处一律改成本会话（建议里的 localSettings 会往用户仓库里写设置文件）。</summary>
    [Fact]
    public async Task StreamJson_AllowForTheSession_SendsTheSuggestedRulesScopedToTheSession()
    {
        const string suggestions = ""","permission_suggestions":[{"type":"addRules","rules":[{"toolName":"Bash","ruleContent":"rm -rf build"}],"behavior":"allow","destination":"localSettings"}]""";
        var transport = new FakeCliAgentTransport(
            [CanUseTool("req-3", suggestions)], onWrite: line => line.Contains("control_response") ? [Result] : []);

        var events = await RunAnsweringAsync(new StreamJsonAdapter(NullLogger<StreamJsonAdapter>.Instance), transport, StreamJsonContext(),
            _ => new CliPermissionDecision(true, CliPermissionOptionKinds.AllowAlways, null));

        events.Single(e => e.Type == CliAgentEventType.PermissionRequest).Permission!.Options
            .ShouldContain(o => o.Kind == CliPermissionOptionKinds.AllowAlways);

        using var reply = JsonDocument.Parse(transport.Written.Single(w => w.Contains("control_response")));
        var rules = reply.RootElement.GetProperty("response").GetProperty("response").GetProperty("updatedPermissions");
        rules.GetArrayLength().ShouldBe(1);
        rules[0].GetProperty("destination").GetString().ShouldBe("session");
        rules[0].GetProperty("rules")[0].GetProperty("ruleContent").GetString().ShouldBe("rm -rf build");
    }

    [Fact]
    public async Task StreamJson_AnsweringTwice_WritesOnce()
    {
        var transport = new FakeCliAgentTransport([CanUseTool("req-4")], onWrite: line => line.Contains("control_response") ? [Result] : []);
        var adapter = new StreamJsonAdapter(NullLogger<StreamJsonAdapter>.Instance);
        var context = StreamJsonContext();

        await foreach (var evt in adapter.RunAsync(transport, context, CancellationToken.None))
        {
            if (evt.Type == CliAgentEventType.PermissionRequest)
            {
                var deny = new CliPermissionDecision(false, null, null);
                (await adapter.AnswerPermissionAsync("req-4", deny, CancellationToken.None)).ShouldBeTrue();
                (await adapter.AnswerPermissionAsync("req-4", deny, CancellationToken.None)).ShouldBeFalse();
                (await adapter.AnswerPermissionAsync("unknown", deny, CancellationToken.None)).ShouldBeFalse();
            }
        }

        transport.Written.Count(w => w.Contains("control_response")).ShouldBe(1);
    }

    /// <summary>没有人值守时「先问我」与此前一样：当场拒绝，不产出权限请求事件。</summary>
    [Fact]
    public async Task StreamJson_AskWithNoOneAttending_DeniesOnTheSpot()
    {
        var transport = new FakeCliAgentTransport([CanUseTool("req-5"), Result], onWrite: _ => []);

        var events = await RunAnsweringAsync(new StreamJsonAdapter(NullLogger<StreamJsonAdapter>.Instance), transport,
            StreamJsonContext(asks: false), _ => null);

        events.ShouldNotContain(e => e.Type == CliAgentEventType.PermissionRequest);
        transport.Written.Single(w => w.Contains("req-5")).ShouldContain("\"behavior\":\"deny\"");
    }

    /// <summary>批准通道只服务「先问我」：别的档位即使上下文说有人值守也照旧当场应答。</summary>
    [Theory]
    [InlineData(CliPermissionMode.Full, "allow")]
    [InlineData(CliPermissionMode.EditWorkspace, "deny")]
    [InlineData(CliPermissionMode.ReadOnly, "deny")]
    public async Task StreamJson_OtherModes_KeepAnsweringOnTheSpot(CliPermissionMode mode, string behavior)
    {
        var transport = new FakeCliAgentTransport([CanUseTool("req-6"), Result], onWrite: _ => []);

        var events = await RunAnsweringAsync(new StreamJsonAdapter(NullLogger<StreamJsonAdapter>.Instance), transport,
            StreamJsonContext(mode), _ => null);

        events.ShouldNotContain(e => e.Type == CliAgentEventType.PermissionRequest);
        transport.Written.Single(w => w.Contains("req-6")).ShouldContain($"\"behavior\":\"{behavior}\"");
    }

    // ── ACP ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 一个 ACP 服务端：prompt 之后先发一个读文件的权限请求（档位放行，当场应答）、再发一个改文件的（交给使用方），
    /// 收到后者的应答才答完 prompt。
    /// </summary>
    private static Func<string, IEnumerable<string>> AcpServer(List<string> permissionAnswers)
    {
        string? promptId = null;
        return line =>
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var id = root.TryGetProperty("id", out var idElement) ? idElement.GetRawText() : null;

            if (!root.TryGetProperty("method", out var methodElement))
            {
                // 客户端对反向请求的应答
                permissionAnswers.Add(line);
                return id == "\"perm-edit\"" && promptId is not null
                    ? [$$$"""{"jsonrpc":"2.0","id":{{{promptId}}},"result":{"stopReason":"end_turn"}}"""]
                    : [];
            }

            return methodElement.GetString() switch
            {
                "initialize" => [$$$"""{"jsonrpc":"2.0","id":{{{id}}},"result":{}}"""],
                "session/new" => [$$$"""{"jsonrpc":"2.0","id":{{{id}}},"result":{"sessionId":"acp-1"}}"""],
                "session/prompt" => Prompt(id!),
                _ => []
            };

            IEnumerable<string> Prompt(string requestId)
            {
                promptId = requestId;
                return
                [
                    """{"jsonrpc":"2.0","id":"perm-read","method":"session/request_permission","params":{"sessionId":"acp-1","toolCall":{"toolCallId":"call-r","title":"Read a.txt","kind":"read"},"options":[{"optionId":"ok","name":"Allow","kind":"allow_once"},{"optionId":"no","name":"Reject","kind":"reject_once"}]}}""",
                    """{"jsonrpc":"2.0","id":"perm-edit","method":"session/request_permission","params":{"sessionId":"acp-1","toolCall":{"toolCallId":"call-e","title":"Edit a.txt","kind":"edit","rawInput":{"path":"a.txt"}},"options":[{"optionId":"ok","name":"Allow","kind":"allow_once"},{"optionId":"always","name":"Always allow","kind":"allow_always"},{"optionId":"no","name":"Reject","kind":"reject_once"}]}}"""
                ];
            }
        };
    }

    [Fact]
    public async Task Acp_AskWithSomeoneAttending_AnswersReadsAndHandsEditsToTheUser()
    {
        var answers = new List<string>();
        var transport = new FakeCliAgentTransport([], onWrite: AcpServer(answers));

        var events = await RunAnsweringAsync(new AcpAdapter(NullLogger<AcpAdapter>.Instance), transport, AcpContext(), evt =>
        {
            // 读的那个已经当场应答，改的这个还没有
            answers.Count.ShouldBe(1);
            return new CliPermissionDecision(true, "always", null);
        });

        var request = events.Single(e => e.Type == CliAgentEventType.PermissionRequest);
        request.CallId.ShouldBe("call-e");
        request.Content.ShouldBe("Edit a.txt");
        request.Input!["path"].ShouldBe("a.txt");
        request.Permission!.Options.Select(o => o.Id).ShouldBe(["ok", "always", "no"]);

        answers.Count.ShouldBe(2);
        answers[0].ShouldContain("\"optionId\":\"ok\"");
        answers[1].ShouldContain("\"id\":\"perm-edit\"");
        answers[1].ShouldContain("\"optionId\":\"always\"");
    }

    [Fact]
    public async Task Acp_DenyWithoutAnOption_PicksTheSingleUseReject()
    {
        var answers = new List<string>();
        var transport = new FakeCliAgentTransport([], onWrite: AcpServer(answers));

        await RunAnsweringAsync(new AcpAdapter(NullLogger<AcpAdapter>.Instance), transport, AcpContext(),
            _ => new CliPermissionDecision(false, null, "timed out"));

        answers.Last().ShouldContain("\"optionId\":\"no\"");
        answers.Last().ShouldNotContain("cancelled");
    }

    [Fact]
    public async Task Acp_AskWithNoOneAttending_RejectsEditsOnTheSpot()
    {
        var answers = new List<string>();
        var transport = new FakeCliAgentTransport([], onWrite: AcpServer(answers));

        var events = await RunAnsweringAsync(new AcpAdapter(NullLogger<AcpAdapter>.Instance), transport, AcpContext(asks: false), _ => null);

        events.ShouldNotContain(e => e.Type == CliAgentEventType.PermissionRequest);
        answers.Count.ShouldBe(2);
        answers[1].ShouldContain("\"optionId\":\"no\"");
    }
}
