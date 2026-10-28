using ModelContextProtocol.Protocol;
using Tnzi.AI.Mcp.Server;

namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// End-to-end smoke tests against a real, locally installed <c>claude</c>.
/// </summary>
/// <remarks>
/// Skipped unless <c>TNZI_RUN_CLI_AGENT_SMOKE=1</c> and the CLI is present, because
/// running them spends the machine owner's account quota. See <see cref="CliSmokeGate"/>.
/// <para>
/// These exist for exactly one reason: the recorded fixtures in
/// <c>StreamJsonAdapterTests</c> keep passing after the real CLI changes its output.
/// The three claims below were established by measurement against a real process and
/// are the ones that break silently when the upstream tool moves.
/// </para>
/// <para>
/// Every turn runs in a fresh workspace under <see cref="TestScratch.Root"/>. A turn in the
/// default tier uses the developer's own claude configuration (and with it their sign-in),
/// so the CLI records the session there; removing the workspace removes that session folder
/// as well (see <see cref="RemoveWorkspace"/>), or each run would leave its test
/// conversations in the developer's session list.
/// </para>
/// </remarks>
public class ClaudeStreamJsonSmokeTests
{
    private const string ProviderKey = "claude";

    /// <summary>
    /// A turn must finish on the <c>result</c> event, not on stdout EOF.
    /// </summary>
    /// <remarks>
    /// stdin stays open so the protocol can answer <c>control_request</c> mid-turn,
    /// which means the CLI never closes stdout on its own. Waiting for EOF measured
    /// at a full 180s hang for a one-word reply; terminating on <c>result</c> brought
    /// the same turn to 6.4s. The bound here is deliberately generous (60s) - this
    /// test is meant to catch "it hangs until the watchdog", not to police latency.
    /// </remarks>
    [SmokeFact(ProviderKey)]
    public async Task SingleTurn_TerminatesOnResultEvent_NotOnEof()
    {
        var (events, outcome, _, elapsed) = await RunTurnAsync("Reply with exactly: OK");

        elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(60));
        outcome.ExitCode.ShouldNotBeNull();
        events.ShouldNotBeEmpty();

        // The reply itself is the model's business and we do not assert its wording.
        // What must hold is that our adapter turned the stream into normalised text.
        events.Where(e => e.Type == CliAgentEventType.Text)
            .Select(e => e.Content)
            .ShouldNotBeEmpty();
    }

    /// <summary>
    /// Every event the real CLI emits must map onto a known event type.
    /// </summary>
    /// <remarks>
    /// The adapter downgrades anything unrecognised to <see cref="CliAgentEventType.Log"/>
    /// rather than throwing, which is the right runtime behaviour but also means protocol
    /// drift is invisible in production. This is the test that makes it visible: a burst
    /// of Log events carrying raw JSON means the upstream format moved.
    /// </remarks>
    [SmokeFact(ProviderKey)]
    public async Task RealOutput_ProducesNoRawJsonFallbackEvents()
    {
        var (events, _, _, _) = await RunTurnAsync("Reply with exactly: OK");

        var rawJsonLogs = events
            .Where(e => e.Type == CliAgentEventType.Log)
            .Where(e => e.Content?.TrimStart().StartsWith('{') == true)
            .ToList();

        rawJsonLogs.ShouldBeEmpty(
            "an unrecognised frame was downgraded to Log, which means the stream-json " +
            "format has moved and the adapter needs updating");
    }

    /// <summary>
    /// A rejected <c>--resume</c> must be classified as
    /// <see cref="CliRunFailureReason.ResumeRejected"/>.
    /// </summary>
    /// <remarks>
    /// This is the most fragile classification in the module and the reason this file
    /// exists. The judgement is made from <b>a stderr phrase plus exit code 1</b>, not
    /// from <c>result.subtype</c> - measurement showed the subtype is the generic
    /// <c>error_during_execution</c>, so it cannot distinguish "this session id is gone"
    /// from "the model errored". A phrase match is inherently brittle across CLI
    /// versions, and nothing but a real run can tell us it has drifted.
    /// <para>
    /// A drifted phrase degrades quietly: the run is still reported as failed, just
    /// under <c>Unknown</c>, so the retry-with-a-fresh-session path stops firing and
    /// every resumed conversation starts silently losing its history.
    /// </para>
    /// </remarks>
    [SmokeFact(ProviderKey)]
    public async Task RejectedResume_IsClassifiedAsResumeRejected()
    {
        // A well-formed but certainly unknown session id: the CLI must reject it.
        var (_, outcome, result, _) = await RunTurnAsync(
            "Reply with exactly: OK",
            resumeSessionId: "00000000-0000-4000-8000-000000000000");

        result.FailureReason.ShouldBe(
            CliRunFailureReason.ResumeRejected,
            $"exit={outcome.ExitCode}, stderr tail was: {outcome.StderrTail}");
    }

    /// <summary>
    /// The sign-in check behind the fallback token must read a boolean from the real CLI.
    /// </summary>
    /// <remarks>
    /// If the status command changes shape, the probe answers "unknown" and the fallback
    /// token gets injected on every run, silently overriding a perfectly good local sign-in.
    /// </remarks>
    [SmokeFact(ProviderKey)]
    public async Task AuthStatus_ReportsABooleanSignInState()
    {
        var provider = CliBuiltInProviders.All[ProviderKey];
        var probe = new CliAuthStatusProbe(NullLogger<CliAuthStatusProbe>.Instance);

        var signedIn = await probe.IsSignedInAsync(LocalHosts.Create(), new CliProcessSpec
        {
            ExecutablePath = CliSmokeGate.ResolveExecutable(ProviderKey)!,
            Arguments = provider.AuthStatusArgs,
            WorkingDirectory = Path.GetTempPath()
        }, CancellationToken.None);

        signedIn.ShouldNotBeNull();
    }

    /// <summary>
    /// With write-back enabled, the real CLI must list and call a consumer tool on the framework's MCP server.
    /// </summary>
    /// <remarks>
    /// The write-back entry is the only MCP configuration the framework composes itself, and nothing but a
    /// real CLI can tell whether the transport type it declares is one the server speaks. It once declared
    /// <c>"sse"</c> (the legacy SSE transport) while the server only maps Streamable HTTP: the CLI could not
    /// connect, the agent saw no write-back tools, and the run still finished as if nothing were wrong.
    /// <para>
    /// The launch also passes <c>--tools ""</c>, the deployment shape that hides the CLI's built-in tools
    /// (they would act on the API host itself) so that only write-back tools remain. That flag must not
    /// hide MCP tools as well.
    /// </para>
    /// </remarks>
    [SmokeFact(ProviderKey)]
    public async Task WriteBack_ListsAndCallsAConsumerTool()
    {
        var nonce = Guid.NewGuid().ToString("N")[..12];
        var calls = new ConcurrentQueue<McpToolCallContext>();

        await using var server = await WriteBackServer.StartAsync();
        using var schema = JsonDocument.Parse(
            """{"type":"object","properties":{"device":{"type":"string"},"command":{"type":"string"}},"required":["device","command"],"additionalProperties":false}""");
        server.Host.ExposeTool(new McpToolDefinition
        {
            Name = "relay_command",
            Description = "Runs a command on a named device and returns its output.",
            InputSchema = schema.RootElement.Clone(),
            Handler = (call, _) =>
            {
                calls.Enqueue(new McpToolCallContext
                {
                    ToolName = call.ToolName,
                    Arguments = call.Arguments.Clone(),
                    Services = call.Services,
                    Caller = call.Caller
                });
                return Task.FromResult(new CallToolResult { Content = [new TextContentBlock { Text = $"relay-output-{nonce}" }] });
            }
        });

        var mcpConfig = new CliMcpConfigComposer(NullLogger<CliMcpConfigComposer>.Instance).Compose(
            bindingMcpConfigJson: null,
            writeBackToken: WriteBackServer.Token,
            new CliWriteBackOptions { Enabled = true, McpEndpoint = server.Endpoint, ServerName = "tnzi", AllowedTools = ["relay_command"] },
            LocalHosts.Create());

        var (events, outcome, _, _) = await RunTurnAsync(
            "Use the relay_command tool to run the command \"uptime\" on the device \"dev-7\". " +
            "Then reply with exactly the text the tool returned and nothing else.",
            mcpConfigJson: mcpConfig,
            extraArgs: ["--tools", ""]);

        // When the model never calls the tool, what it saw and said is the only clue: keep it in the failure.
        var transcript = string.Join(Environment.NewLine, events.Select(e => $"[{e.Type}] {e.Tool} {e.Status} {e.Content ?? e.Output}"));
        var call = calls.ShouldHaveSingleItem(
            $"exit={outcome.ExitCode}, stderr tail was: {outcome.StderrTail}{Environment.NewLine}events:{Environment.NewLine}{transcript}");
        call.Arguments.GetProperty("device").GetString().ShouldBe("dev-7");
        call.Arguments.GetProperty("command").GetString().ShouldBe("uptime");
        call.Caller.IsRunScoped.ShouldBeTrue();
        call.Caller.RunId.ShouldBe(WriteBackServer.RunId);
        call.Caller.CreatorId.ShouldBe(WriteBackServer.CreatorId);
        call.Caller.ThreadId.ShouldBe(WriteBackServer.ThreadId);

        events.ShouldContain(e => e.Type == CliAgentEventType.ToolUse && e.Tool != null && e.Tool.Contains("relay_command"));
        string.Concat(events.Where(e => e.Type == CliAgentEventType.Text).Select(e => e.Content)).ShouldContain(nonce);
    }

    /// <summary>
    /// A write-back tool call that runs past 60 seconds completes when <see cref="CliWriteBackOptions.ToolCallTimeout"/> is set,
    /// and the progress heartbeats it produces on the way are not reported as unhandled events.
    /// </summary>
    /// <remarks>
    /// The entry's <c>timeout</c> is also how long claude's HTTP transport waits for a response to start, 60 seconds
    /// when absent. Served directly, the server sends the headers of a tool call at once and a long call is unaffected;
    /// behind a proxy that holds the response until it is complete, every call longer than that is cut off with
    /// "The operation timed out." while the consumer's tool is still working. The server here holds its POST responses
    /// the same way, so this test fails without <see cref="CliWriteBackOptions.ToolCallTimeout"/>. The field name, its
    /// unit (milliseconds) and what it governs all come from the CLI, so only a real run can tell they still hold.
    /// The tool waits 75 seconds: past the default, and long enough for at least two <c>tool_progress</c> heartbeats
    /// (about one every 30 seconds).
    /// </remarks>
    [SmokeFact(ProviderKey)]
    public async Task WriteBack_ToolCallTimeout_LetsACallRunPastSixtySeconds()
    {
        var nonce = Guid.NewGuid().ToString("N")[..12];
        var completedCalls = 0;

        await using var server = await WriteBackServer.StartAsync(holdPostResponses: true);
        using var schema = JsonDocument.Parse(
            """{"type":"object","properties":{"target":{"type":"string"}},"required":["target"],"additionalProperties":false}""");
        server.Host.ExposeTool(new McpToolDefinition
        {
            Name = "slow_scan",
            Description = "Scans a target. Takes a little over a minute and returns the scan id.",
            InputSchema = schema.RootElement.Clone(),
            Handler = async (_, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(75), cancellationToken);
                Interlocked.Increment(ref completedCalls);
                return new CallToolResult { Content = [new TextContentBlock { Text = $"scan-{nonce}" }] };
            }
        });

        var mcpConfig = new CliMcpConfigComposer(NullLogger<CliMcpConfigComposer>.Instance).Compose(
            bindingMcpConfigJson: null,
            writeBackToken: WriteBackServer.Token,
            new CliWriteBackOptions
            {
                Enabled = true, McpEndpoint = server.Endpoint, ServerName = "tnzi", AllowedTools = ["slow_scan"],
                ToolCallTimeout = TimeSpan.FromMinutes(2)
            },
            LocalHosts.Create());

        var (events, outcome, _, _) = await RunTurnAsync(
            "Use the slow_scan tool on the target \"disk-c\" exactly once and wait for it to finish. " +
            "Then reply with exactly the text the tool returned and nothing else.",
            mcpConfigJson: mcpConfig,
            extraArgs: ["--tools", ""]);

        var transcript = string.Join(Environment.NewLine, events.Select(e => $"[{e.Type}] {e.Tool} {e.Status} {e.Content ?? e.Output}"));
        completedCalls.ShouldBe(1,
            $"exit={outcome.ExitCode}, stderr tail was: {outcome.StderrTail}{Environment.NewLine}events:{Environment.NewLine}{transcript}");
        string.Concat(events.Where(e => e.Type == CliAgentEventType.Text).Select(e => e.Content)).ShouldContain(nonce, customMessage: transcript);
        events.ShouldNotContain(e => e.Type == CliAgentEventType.Log && e.Content != null && e.Content.Contains("tool_progress"));
    }

    /// <summary>
    /// A turn in the default tier leaves nothing in the host account's projects directory once its workspace is removed.
    /// </summary>
    /// <remarks>
    /// The default tier keeps the developer's own configuration directory, so the CLI records every smoke session there
    /// (transcript and auto memory) in a folder named after the flattened working-directory path, and nothing else ever
    /// deletes it: the developer's session list fills with test conversations. <see cref="RemoveWorkspace"/> finds that
    /// folder by the workspace's directory name, which holds only while the CLI keeps the transcripts in
    /// <see cref="HostProjectsDirectory"/> and names the folder after the path. This test checks both against a real run,
    /// so a CLI that moves or renames them fails here instead of quietly leaving the folders behind again.
    /// </remarks>
    [SmokeFact(ProviderKey)]
    public async Task DefaultTier_LeavesNoSessionFolderInTheHostProjectsDirectory()
    {
        var workspace = ThrowawayWorkspace();
        string sessionId;

        try
        {
            var (events, outcome, result, _) = await RunTurnAsync("Reply with exactly: OK", workspace: workspace);
            sessionId = events.Select(e => e.SessionId).LastOrDefault(s => !string.IsNullOrWhiteSpace(s))
                .ShouldNotBeNull($"exit={outcome.ExitCode}, error={result.Error}, stderr tail was: {outcome.StderrTail}");

            var transcripts = Transcripts(HostProjectsDirectory, sessionId);
            transcripts.ShouldNotBeEmpty("the default tier keeps the transcript in the host account's projects directory");

            var folders = SessionFolders(HostProjectsDirectory, workspace);
            transcripts.ShouldAllBe(
                transcript => folders.Contains(Path.GetDirectoryName(transcript)!),
                "the removal finds the session folder by the workspace's directory name");
        }
        finally
        {
            RemoveWorkspace(workspace);
        }

        Transcripts(HostProjectsDirectory, sessionId).ShouldBeEmpty("the session folder goes with the workspace");
        SessionFolders(HostProjectsDirectory, workspace).ShouldBeEmpty();
    }

    /// <summary>
    /// With a per-workspace config directory the session transcript lives inside the workspace, and the next turn of the
    /// thread resumes from it.
    /// </summary>
    /// <remarks>
    /// The CLI keeps the full record of every session (tool results included) under its config directory. This tier puts that
    /// directory in the workspace's run root so the record goes when the workspace goes; what only a real CLI can show is that
    /// it really writes there (and not into the host account's own directory) and that <c>--resume</c> still finds the session.
    /// Needs a credential in the environment: the directory never contains a stored sign-in.
    /// </remarks>
    [SmokeCredentialFact(ProviderKey)]
    public async Task PerWorkspaceConfigDirectory_KeepsTheTranscriptInTheWorkspace_AndResumesFromIt()
    {
        var workspace = PerThreadWorkspace();
        var word = "kiwi" + Guid.NewGuid().ToString("N")[..6];

        try
        {
            var (firstEvents, firstOutcome, firstResult, _) = await RunTurnAsync(
                $"Remember this code word: {word}. Reply with exactly: OK",
                workspace: workspace, isolation: CliUserConfigIsolation.PerWorkspaceConfigDirectory);
            var sessionId = firstEvents.Select(e => e.SessionId).LastOrDefault(s => !string.IsNullOrWhiteSpace(s))
                .ShouldNotBeNull($"exit={firstOutcome.ExitCode}, error={firstResult.Error}, stderr tail was: {firstOutcome.StderrTail}");

            var configDirectory = CliWorkspaceLayout.ResolveWorkspaceConfigDirectory(workspace.RootDirectory, CliBuiltInProviders.All[ProviderKey], CliHostPaths.Native);
            Transcripts(Path.Combine(configDirectory, "projects"), sessionId)
                .ShouldNotBeEmpty("the transcript is written inside the workspace");

            Transcripts(HostProjectsDirectory, sessionId)
                .ShouldBeEmpty("nothing of this session lands in the host account's own directory");

            var (secondEvents, secondOutcome, secondResult, _) = await RunTurnAsync(
                "What was the code word? Reply with just the word.",
                resumeSessionId: sessionId, workspace: workspace, isolation: CliUserConfigIsolation.PerWorkspaceConfigDirectory);

            secondResult.FailureReason.ShouldNotBe(CliRunFailureReason.ResumeRejected, $"stderr tail was: {secondOutcome.StderrTail}");
            string.Concat(secondEvents.Where(e => e.Type == CliAgentEventType.Text).Select(e => e.Content)).ShouldContain(word);
        }
        finally
        {
            RemoveWorkspace(workspace);
        }
    }

    /// <summary>
    /// An image and a PDF sent with the prompt reach the model as content blocks it can read.
    /// </summary>
    /// <remarks>
    /// The user frame carries them as <c>image</c> and <c>document</c> blocks ahead of the text. A frame the CLI does not
    /// accept fails the whole turn, and one it accepts but drops the blocks from answers from the text alone; the model
    /// naming the colour and the code word is the only evidence that both arrived.
    /// </remarks>
    [SmokeFact(ProviderKey)]
    public async Task Attachments_AnImageAndAPdf_ReachTheModel()
    {
        var code = "QX" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        var (events, outcome, result, _) = await RunTurnAsync(
            "Two files are attached: an image and a PDF. Reply in exactly this form and nothing else: " +
            "COLOR=<the single colour filling the image, one lowercase English word>; CODE=<the code printed in the PDF>",
            attachments:
            [
                new CliPromptAttachment("image/png", "swatch.png", SmokeFiles.SolidPng(64, 64, 220, 20, 20)),
                new CliPromptAttachment("application/pdf", "note.pdf", SmokeFiles.OneLinePdf($"Code: {code}"))
            ]);

        var reply = string.Concat(events.Where(e => e.Type == CliAgentEventType.Text).Select(e => e.Content));
        result.Status.ShouldBe(CliRunStatus.Completed, $"exit={outcome.ExitCode}, error={result.Error}, stderr tail was: {outcome.StderrTail}");
        reply.ShouldContain("red", Case.Insensitive, reply);
        reply.ShouldContain(code, Case.Insensitive, reply);
    }

    /// <summary>
    /// Resuming at an earlier entry forks a session that has forgotten the turns after it, and leaves the original intact.
    /// </summary>
    /// <remarks>
    /// Editing or regenerating a message in a thread cuts the CLI session back to the end of the last turn that stays:
    /// <c>--resume &lt;session&gt; --resume-session-at &lt;entry&gt; --fork-session</c>. What only a real CLI can show is that
    /// the entry id the adapter records (the last conversation frame's <c>uuid</c>) is one it accepts as a cut point, that
    /// the fork really does not know the dropped turn, and that the original session still does (a failed edit falls back
    /// to it).
    /// </remarks>
    [SmokeFact(ProviderKey)]
    public async Task ResumeAtAnEntry_ForksASessionWithoutTheDroppedTurn_AndLeavesTheOriginal()
    {
        var workspace = ThrowawayWorkspace();
        var kept = "kiwi" + Guid.NewGuid().ToString("N")[..6];
        var dropped = "mango" + Guid.NewGuid().ToString("N")[..6];

        try
        {
            var (firstEvents, firstOutcome, first, _) = await RunTurnAsync(
                $"Remember this code word: {kept}. Reply with exactly: OK", workspace: workspace);
            var session = firstEvents.Select(e => e.SessionId).LastOrDefault(s => !string.IsNullOrWhiteSpace(s))
                .ShouldNotBeNull($"exit={firstOutcome.ExitCode}, error={first.Error}, stderr tail was: {firstOutcome.StderrTail}");
            var cutPoint = first.SessionEntryId.ShouldNotBeNull("the adapter records where the turn ended");

            var (_, secondOutcome, second, _) = await RunTurnAsync(
                $"The code word has changed to {dropped}; forget the old one. Reply with exactly: OK",
                resumeSessionId: session, workspace: workspace);
            second.Status.ShouldBe(CliRunStatus.Completed, $"stderr tail was: {secondOutcome.StderrTail}");

            var (forkEvents, forkOutcome, fork, _) = await RunTurnAsync(
                "What is the code word? Reply with just the word.",
                resumeSessionId: session, resumeAtEntryId: cutPoint, workspace: workspace);

            fork.FailureReason.ShouldNotBe(CliRunFailureReason.ResumeRejected, $"stderr tail was: {forkOutcome.StderrTail}");
            var forkReply = string.Concat(forkEvents.Where(e => e.Type == CliAgentEventType.Text).Select(e => e.Content));
            forkReply.ShouldContain(kept, customMessage: forkReply);
            forkReply.ShouldNotContain(dropped, customMessage: "the dropped turn is not part of the fork");
            forkEvents.Select(e => e.SessionId).LastOrDefault(s => !string.IsNullOrWhiteSpace(s))
                .ShouldNotBe(session, "the cut goes into a new session");

            var (originalEvents, _, _, _) = await RunTurnAsync(
                "What is the code word now? Reply with just the word.", resumeSessionId: session, workspace: workspace);
            string.Concat(originalEvents.Where(e => e.Type == CliAgentEventType.Text).Select(e => e.Content))
                .ShouldContain(dropped, customMessage: "the original session still has every turn");
        }
        finally
        {
            RemoveWorkspace(workspace);
        }
    }

    /// <summary>
    /// An image a write-back tool returns comes back as an image block of the tool result, and the model sees it.
    /// </summary>
    /// <remarks>
    /// A consumer tool that returns a screenshot is the reason tool results keep their blocks: the chat shows the image on
    /// the tool card, and the run stores it as an attachment. Whether the CLI passes MCP image content through to its
    /// stream-json <c>tool_result</c> as an image block (rather than dropping it or flattening it to text) is the CLI's
    /// behaviour, so only a real run can tell.
    /// </remarks>
    [SmokeFact(ProviderKey)]
    public async Task WriteBack_AToolThatReturnsAnImage_ComesBackAsAnImageBlock()
    {
        var calls = 0;
        await using var server = await WriteBackServer.StartAsync();
        using var schema = JsonDocument.Parse("""{"type":"object","properties":{},"additionalProperties":false}""");
        server.Host.ExposeTool(new McpToolDefinition
        {
            Name = "take_screenshot",
            Description = "Takes a screenshot of the screen and returns it as an image.",
            InputSchema = schema.RootElement.Clone(),
            Handler = (_, _) =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(new CallToolResult
                {
                    Content =
                    [
                        new TextContentBlock { Text = "captured" },
                        ImageContentBlock.FromBytes(SmokeFiles.SolidPng(64, 64, 220, 20, 20), "image/png")
                    ]
                });
            }
        });

        var mcpConfig = new CliMcpConfigComposer(NullLogger<CliMcpConfigComposer>.Instance).Compose(
            bindingMcpConfigJson: null,
            writeBackToken: WriteBackServer.Token,
            new CliWriteBackOptions { Enabled = true, McpEndpoint = server.Endpoint, ServerName = "tnzi", AllowedTools = ["take_screenshot"] },
            LocalHosts.Create());

        var (events, outcome, _, _) = await RunTurnAsync(
            "Use the take_screenshot tool once. Then reply with the single colour filling the screenshot, " +
            "as one lowercase English word and nothing else.",
            mcpConfigJson: mcpConfig,
            extraArgs: ["--tools", ""]);

        var transcript = string.Join(Environment.NewLine, events.Select(e => $"[{e.Type}] {e.Tool} {e.Status} {e.Content ?? e.Output}"));
        calls.ShouldBe(1, $"exit={outcome.ExitCode}, stderr tail was: {outcome.StderrTail}{Environment.NewLine}events:{Environment.NewLine}{transcript}");

        var toolResult = events.Single(e => e.Type == CliAgentEventType.ToolResult);
        var blocks = toolResult.OutputBlocks.ShouldNotBeNull(transcript);
        var image = blocks.Where(b => b.Type == "image").ShouldHaveSingleItem(transcript);
        image.MediaType.ShouldBe("image/png");
        image.Data.ShouldNotBeNullOrEmpty();
        toolResult.Output.ShouldNotBeNull().ShouldNotContain(image.Data!, customMessage: "the text form leaves the bytes out");

        string.Concat(events.Where(e => e.Type == CliAgentEventType.Text).Select(e => e.Content))
            .ShouldContain("red", Case.Insensitive, transcript);
    }

    /// <summary>
    /// The <c>rate_limit_event</c> a real run reports carries the <c>rate_limit_info</c> shape the usage-limit detection reads.
    /// </summary>
    /// <remarks>
    /// The refusal itself (an assistant message with <c>api_error: "usage_limit_reached"</c>) cannot be produced on demand,
    /// so <c>StreamJsonUsageLimitTests</c> plays the frames the CLI's own schema describes. What a real run can show is the
    /// part shared with every turn: a subscription sign-in reports its limit window with a status and a reset time in the
    /// same <c>rate_limit_info</c> shape. A run signed in with an API key reports no limit windows; this test expects a
    /// subscription sign-in.
    /// </remarks>
    [SmokeFact(ProviderKey)]
    public async Task RateLimitEvent_HasTheShapeTheUsageLimitDetectionReads()
    {
        StreamJsonRateLimit? info = null;
        var (events, outcome, result, _) = await RunTurnAsync("Reply with exactly: OK", inspect: adapter => info = adapter.LastRateLimitInfo);

        result.Status.ShouldBe(CliRunStatus.Completed, $"stderr tail was: {outcome.StderrTail}");
        events.ShouldContain(e => e.Type == CliAgentEventType.Status && e.Status == "rate_limit",
            "a subscription sign-in reports its limit window on the first response");
        info.ShouldNotBeNull();
        info.Status.ShouldBeOneOf("allowed", "allowed_warning", "rejected");
        info.ResetsAt.ShouldNotBeNull("the window says when it resets");
        info.ResetsAt.Value.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    /// <summary>
    /// A file a write-back tool hands over (an MCP <c>resource_link</c>) comes back as a file block of the tool result.
    /// </summary>
    /// <remarks>
    /// claude does not pass the block through: it flattens it to the text <c>[Resource link: name] uri</c> (the media type
    /// and the size are dropped), and the adapter turns that line back into a file block. Only a real run shows the
    /// flattened form is still the one the adapter recognises.
    /// </remarks>
    [SmokeFact(ProviderKey)]
    public async Task WriteBack_AFileTheToolHandsOver_ComesBackAsAFileBlock()
    {
        await using var server = await WriteBackServer.StartAsync();
        using var schema = JsonDocument.Parse("""{"type":"object","properties":{},"additionalProperties":false}""");
        server.Host.ExposeTool(new McpToolDefinition
        {
            Name = "hand_over_file",
            Description = "Prepares the report and hands it over to the user.",
            InputSchema = schema.RootElement.Clone(),
            Handler = (_, _) => Task.FromResult(new CallToolResult
            {
                Content =
                [
                    new TextContentBlock { Text = "The report is ready." },
                    new ResourceLinkBlock { Uri = "https://files.example.com/u/42/report.pdf", Name = "report.pdf", MimeType = "application/pdf" }
                ]
            })
        });

        var mcpConfig = new CliMcpConfigComposer(NullLogger<CliMcpConfigComposer>.Instance).Compose(
            bindingMcpConfigJson: null,
            writeBackToken: WriteBackServer.Token,
            new CliWriteBackOptions { Enabled = true, McpEndpoint = server.Endpoint, ServerName = "tnzi", AllowedTools = ["hand_over_file"] },
            LocalHosts.Create());

        var (events, outcome, _, _) = await RunTurnAsync(
            "Use the hand_over_file tool once, then reply with exactly: done", mcpConfigJson: mcpConfig, extraArgs: ["--tools", ""]);

        var transcript = string.Join(Environment.NewLine, events.Select(e => $"[{e.Type}] {e.Tool} {e.Content ?? e.Output}"));
        var toolResult = events.SingleOrDefault(e => e.Type == CliAgentEventType.ToolResult)
            .ShouldNotBeNull($"stderr tail was: {outcome.StderrTail}{Environment.NewLine}{transcript}");
        var file = toolResult.OutputBlocks.ShouldNotBeNull(transcript).Where(b => b.Type == CliContentBlock.FileType)
            .ShouldHaveSingleItem(toolResult.Output);
        file.Name.ShouldBe("report.pdf");
        file.Uri.ShouldBe("https://files.example.com/u/42/report.pdf");
    }

    private static CliLaunchEnvironmentComposer Composer()
        => new(new CliAuthStatusProbe(NullLogger<CliAuthStatusProbe>.Instance), NullLogger<CliLaunchEnvironmentComposer>.Instance);

    /// <summary>Drive one real turn end to end and hand back what it produced.</summary>
    /// <remarks>
    /// The adapter is stateful and one-shot, so <c>GetResult</c> is called here on the
    /// very instance that drove the session. Calling it on a fresh instance compiles and
    /// returns a plausible-looking result built from nothing but the process outcome -
    /// which is how the first version of this test managed to report a classification
    /// failure that the production path does not have.
    /// </remarks>
    /// <param name="prompt">The turn's prompt.</param>
    /// <param name="resumeSessionId">A session to resume.</param>
    /// <param name="mcpConfigJson">A managed MCP config, delivered as a file the way the workspace preparer does.</param>
    /// <param name="extraArgs">Deployment-level extra arguments.</param>
    /// <param name="workspace">
    /// A workspace the caller removes with <see cref="RemoveWorkspace"/> (multi-turn tests, or a test that looks at what the
    /// turn left behind). When null a throwaway one is created and removed afterwards.
    /// </param>
    /// <param name="isolation">The provider's configuration isolation tier; null keeps the deployment default.</param>
    /// <param name="attachments">Images and PDFs sent with the prompt.</param>
    /// <param name="resumeAtEntryId">With <paramref name="resumeSessionId"/>: cut the session back to this entry and fork it.</param>
    /// <param name="inspect">Looks at the adapter after the turn (state the events do not carry).</param>
    private static async Task<(List<CliAgentEvent> Events, CliSessionOutcome Outcome, CliAgentResult Result, TimeSpan Elapsed)>
        RunTurnAsync(
            string prompt, string? resumeSessionId = null, string? mcpConfigJson = null, IReadOnlyList<string>? extraArgs = null,
            CliWorkspace? workspace = null, CliUserConfigIsolation? isolation = null,
            IReadOnlyList<CliPromptAttachment>? attachments = null, string? resumeAtEntryId = null,
            Action<StreamJsonAdapter>? inspect = null)
    {
        var provider = isolation is { } tier
            ? CliBuiltInProviders.All[ProviderKey] with { UserConfigIsolation = tier }
            : CliBuiltInProviders.All[ProviderKey];
        var executablePath = CliSmokeGate.ResolveExecutable(ProviderKey)!;

        // A throwaway working directory: the CLI is free to write into it, and an
        // isolated one keeps the smoke test from touching this repository.
        var ownsWorkspace = workspace is null;
        workspace ??= ThrowawayWorkspace();
        var workDirectory = workspace.WorkDirectory;

        // A per-workspace config directory has no stored sign-in: the credential has to come from the environment.
        var options = new CliAgentOptions
        {
            Enabled = true,
            EnvironmentWhitelist = isolation == CliUserConfigIsolation.PerWorkspaceConfigDirectory
                ? [.. CliSmokeGate.PresentCredentialVariables()]
                : []
        };

        try
        {
            // Compose the launch the way the executor does, so the deployment-default
            // isolation arguments are part of what the real CLI has to accept.
            var host = LocalHosts.Create(options);
            var launchEnvironment = await Composer().ComposeAsync(
                provider, executablePath, workspace, host, options, CliLaunchProfile.Local, CancellationToken.None);

            // Delivered the way the workspace preparer delivers it: a file the CLI is pointed at.
            string? mcpConfigPath = null;
            if (mcpConfigJson is not null)
            {
                mcpConfigPath = Path.Combine(workDirectory, CliWorkspaceLayout.McpConfigFileName);
                await File.WriteAllTextAsync(mcpConfigPath, mcpConfigJson);
            }

            var context = new CliAgentLaunchContext
            {
                Provider = provider,
                ExecutablePath = executablePath,
                Prompt = prompt,
                WorkingDirectory = workDirectory,
                ResumeSessionId = resumeSessionId,
                ResumeAtEntryId = resumeAtEntryId,
                Attachments = attachments ?? [],
                McpConfigPath = mcpConfigPath,
                ManagedArgs = launchEnvironment.Args,
                OwnedArgs = launchEnvironment.OwnedArgs,
                ExtraArgs = extraArgs ?? [],
                Environment = launchEnvironment.Environment,
                EnvironmentWhitelist = options.EnvironmentWhitelist
            };

            var adapter = new StreamJsonAdapter(NullLogger<StreamJsonAdapter>.Instance);

            // A hard ceiling so a hung CLI fails the test instead of the whole run.
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));

            var events = new List<CliAgentEvent>();
            var stopwatch = Stopwatch.StartNew();

            await using var process = await host.StartAsync(adapter.BuildProcess(context), cts.Token);
            try
            {
                await foreach (var evt in adapter.RunAsync(process.Transport, context, cts.Token))
                {
                    events.Add(evt);
                }
            }
            catch (OperationCanceledException)
            {
                // Leave it to the assertions: an empty event list plus the elapsed
                // ceiling says more than an exception from inside the helper would.
            }
            finally
            {
                stopwatch.Stop();
                await process.TerminateAsync(CancellationToken.None);
            }

            var outcome = new CliSessionOutcome
            {
                ExitCode = process.ExitCode,
                StderrTail = process.Transport.StderrTail,
                Elapsed = stopwatch.Elapsed
            };

            var result = adapter.GetResult(outcome);
            inspect?.Invoke(adapter);
            return (events, outcome, result, stopwatch.Elapsed);
        }
        finally
        {
            if (ownsWorkspace)
            {
                RemoveWorkspace(workspace);
            }
        }
    }

    /// <summary>
    /// The <c>projects</c> directory of the host account's own claude configuration, where a turn that does not relocate
    /// the configuration directory records its session.
    /// </summary>
    /// <remarks>
    /// The launch never passes the host's own <c>CLAUDE_CONFIG_DIR</c> through (it is neither in the environment baseline
    /// nor in any whitelist these tests use), so such a turn always uses the directory under the user profile.
    /// </remarks>
    private static string HostProjectsDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

    /// <summary>
    /// Remove a workspace together with the session folder the CLI keeps for it in <see cref="HostProjectsDirectory"/>.
    /// </summary>
    /// <remarks>
    /// Only folders carrying this workspace's own directory name are touched: that name ends in a GUID created by this
    /// test, so nothing the test did not create can match. A relocated configuration directory lives inside the workspace
    /// and goes with it.
    /// </remarks>
    private static void RemoveWorkspace(CliWorkspace workspace)
    {
        foreach (var folder in SessionFolders(HostProjectsDirectory, workspace))
        {
            TestScratch.Delete(folder);
        }

        TestScratch.Delete(workspace.RootDirectory);
    }

    /// <summary>
    /// The folders under a CLI <c>projects</c> directory that belong to a workspace.
    /// </summary>
    /// <remarks>
    /// claude names the folder after the working directory's full path with every character other than a letter or a
    /// digit replaced by <c>-</c>, so the workspace's directory name (letters, digits and dashes only) appears in it as is.
    /// </remarks>
    private static List<string> SessionFolders(string projectsDirectory, CliWorkspace workspace)
    {
        var workspaceName = Path.GetFileName(workspace.RootDirectory);
        return Directory.Exists(projectsDirectory)
            ? Directory.EnumerateDirectories(projectsDirectory)
                .Where(folder => Path.GetFileName(folder).Contains(workspaceName, StringComparison.OrdinalIgnoreCase))
                .ToList()
            : [];
    }

    /// <summary>
    /// The session's transcript files under a CLI <c>projects</c> directory: claude keeps them one level down,
    /// in a directory named after the working directory, so no recursive walk is needed.
    /// </summary>
    private static List<string> Transcripts(string projectsDirectory, string sessionId)
        => Directory.Exists(projectsDirectory)
            ? Directory.EnumerateDirectories(projectsDirectory)
                .Select(project => Path.Combine(project, sessionId + ".jsonl"))
                .Where(File.Exists)
                .ToList()
            : [];

    /// <summary>A throwaway workspace whose root is also its working directory.</summary>
    private static CliWorkspace ThrowawayWorkspace()
    {
        var directory = TestScratch.CreateDirectory("smoke");
        return new CliWorkspace
        {
            RootDirectory = directory,
            WorkDirectory = directory,
            OutputDirectory = directory,
            LogDirectory = directory
        };
    }

    /// <summary>A workspace in the per-thread shape: the working directory is <c>workdir</c> under the run root.</summary>
    private static CliWorkspace PerThreadWorkspace()
    {
        var root = TestScratch.CreateDirectory("smoke-thread");
        var workspace = new CliWorkspace
        {
            RootDirectory = root,
            WorkDirectory = Path.Combine(root, CliWorkspaceLayout.WorkDirectoryName),
            OutputDirectory = Path.Combine(root, CliWorkspaceLayout.OutputDirectoryName),
            LogDirectory = Path.Combine(root, CliWorkspaceLayout.LogDirectoryName)
        };
        Directory.CreateDirectory(workspace.WorkDirectory);
        return workspace;
    }
}
