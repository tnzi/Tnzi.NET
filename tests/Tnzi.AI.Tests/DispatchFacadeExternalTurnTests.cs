using System.Text;
using Tnzi.Exceptions;

namespace Tnzi.AI.Tests;

/// <summary>
/// A chat turn on an agent bound to an external CLI runtime, as the dispatch facade hands it over and streams it back:
/// the run id up front, tool calls with their input and result, sequence numbers to reattach from, attachments,
/// and the consumer's turn handlers.
/// </summary>
/// <remarks>
/// The reply itself is not written here any more: the run writes it into the thread when it ends (Tnzi.AI.Cli), so it is
/// there whether or not anyone is still connected. The facade's terminal chunk carries the id the run recorded.
/// </remarks>
public class DispatchFacadeExternalTurnTests
{
    private static readonly Guid AgentId = Guid.NewGuid();
    private static readonly Guid ExistingThreadId = Guid.NewGuid();

    // ---------------------------------------------------------------- the stream

    [Fact]
    public async Task RunStreamingAsync_FirstChunk_CarriesTheRunIdAndTheStoredUserMessage()
    {
        var threads = new RecordingThreadService();
        var dispatcher = new ScriptedDispatcher([Text(1, "hello")]);
        var facade = CreateFacade(dispatcher, threads);

        var chunks = await CollectAsync(facade.RunStreamingAsync(Request("hi", ExistingThreadId)));

        var first = chunks[0];
        first.EventType.ShouldBe(RunStreamEventTypes.RunStarted);
        first.RunId.ShouldBe(dispatcher.RunId);
        first.UserMessageId.ShouldBe(threads.Saved.Single(m => m.Role == "user").Id,
            "the client keys the edit button of its user bubble on this id");
    }

    [Fact]
    public async Task RunStreamingAsync_TerminalChunk_CarriesTheReplyTheRunWrote_AndTheFacadeWritesNoReply()
    {
        var threads = new RecordingThreadService();
        var replyId = Guid.NewGuid();
        var dispatcher = new ScriptedDispatcher([Text(1, "hello")]) { AssistantMessageId = replyId };
        var facade = CreateFacade(dispatcher, threads);

        var chunks = await CollectAsync(facade.RunStreamingAsync(Request("hi", ExistingThreadId)));

        var terminal = chunks[^1];
        terminal.FinishReason.ShouldBe(FinishReasons.Stop);
        terminal.AssistantMessageId.ShouldBe(replyId);
        terminal.RunId.ShouldBe(dispatcher.RunId);
        threads.Saved.Select(m => m.Role).ShouldBe(["user"], "the run writes its own reply when it ends, connected client or not");
    }

    [Fact]
    public async Task RunStreamingAsync_EveryEventChunk_CarriesItsSequence()
    {
        var dispatcher = new ScriptedDispatcher([Text(1, "a"), ToolUse(2, "c1", "Read"), ToolResult(3, "c1", "done"), Text(4, "b")]);
        var facade = CreateFacade(dispatcher, new RecordingThreadService());

        var chunks = await CollectAsync(facade.RunStreamingAsync(Request("hi", ExistingThreadId)));

        chunks.Where(c => c.Sequence.HasValue).Select(c => c.Sequence!.Value).ShouldBe([1, 2, 3, 4]);
    }

    [Fact]
    public async Task RunStreamingAsync_ToolCall_ArrivesTwiceUnderOneId_WithInputThenResult()
    {
        using var input = JsonDocument.Parse("""{"path":"a.txt","edits":[{"old":"x","new":"y"}]}""");
        var dispatcher = new ScriptedDispatcher(
        [
            Text(1, "Let me look."),
            new CliAgentEvent
            {
                Type = CliAgentEventType.ToolUse, Sequence = 2, CallId = "call-1", Tool = "mcp__tnzi__screenshot",
                Input = JsonSerializer.Deserialize<Dictionary<string, object?>>(input.RootElement.GetRawText()),
                CreatedAt = new DateTime(2026, 10, 19, 1, 0, 0, DateTimeKind.Utc)
            },
            new CliAgentEvent
            {
                Type = CliAgentEventType.ToolResult, Sequence = 3, CallId = "call-1", Tool = "mcp__tnzi__screenshot", Status = "completed",
                OutputBlocks =
                [
                    new CliContentBlock { Type = CliContentBlock.TextType, Text = "captured" },
                    new CliContentBlock { Type = CliContentBlock.ImageType, MediaType = "image/png", AttachmentId = Guid.Parse("11111111-1111-1111-1111-111111111111") }
                ],
                CreatedAt = new DateTime(2026, 10, 19, 1, 0, 4, DateTimeKind.Utc)
            },
            Text(4, "It shows a login page.")
        ]);
        var facade = CreateFacade(dispatcher, new RecordingThreadService());

        var chunks = await CollectAsync(facade.RunStreamingAsync(Request("hi", ExistingThreadId)));

        var calls = chunks.Where(c => c.ToolCalls != null).SelectMany(c => c.ToolCalls!).ToList();
        calls.Count.ShouldBe(2);

        var started = calls[0];
        started.Id.ShouldBe("call-1");
        started.Name.ShouldBe("mcp__tnzi__screenshot");
        started.Status.ShouldBe(ToolCallStatuses.Running);
        started.Input.ShouldNotBeNull();
        started.Input.Value.GetProperty("edits")[0].GetProperty("new").GetString().ShouldBe("y",
            "nested arguments stay structured, not a string of JSON");
        started.TextOffset.ShouldBe("Let me look.".Length);

        var finished = calls[1];
        finished.Id.ShouldBe("call-1");
        finished.Status.ShouldBe(ToolCallStatuses.Completed);
        finished.IsSuccess.ShouldBeTrue();
        finished.DurationMs.ShouldBe(4000);
        var result = finished.Result.ShouldNotBeNull();
        result.Select(b => b.Type).ShouldBe(["text", "image"]);
        result[1].AttachmentId.ShouldBe(Guid.Parse("11111111-1111-1111-1111-111111111111"));

        // the call sits between two paragraphs: the text after it starts a new block
        string.Concat(chunks.Where(c => c.Text != null).Select(c => c.Text))
            .ShouldBe("Let me look.\n\nIt shows a login page.");
    }

    [Fact]
    public async Task RunStreamingAsync_FailedToolCall_IsMarkedFailedWithItsMessage()
    {
        var dispatcher = new ScriptedDispatcher(
        [
            ToolUse(1, "c1", "Bash"),
            new CliAgentEvent { Type = CliAgentEventType.ToolResult, Sequence = 2, CallId = "c1", Status = "failed", Output = "permission denied" }
        ]);
        var facade = CreateFacade(dispatcher, new RecordingThreadService());

        var chunks = await CollectAsync(facade.RunStreamingAsync(Request("hi", ExistingThreadId)));

        var finished = chunks.Where(c => c.ToolCalls != null).SelectMany(c => c.ToolCalls!).Last();
        finished.Status.ShouldBe(ToolCallStatuses.Failed);
        finished.IsSuccess.ShouldBeFalse();
        finished.Error.ShouldBe("permission denied");
        finished.Name.ShouldBe("Bash", "a result without a name is matched back to the call that started it");
    }

    // ---------------------------------------------------------------- reattach, cancel, active runs

    [Fact]
    public async Task AttachStreamingAsync_FromASequence_SkipsWhatTheClientHas_ButKeepsTheReplyShape()
    {
        var dispatcher = new ScriptedDispatcher([Text(1, "first"), ToolUse(2, "c1", "Read"), ToolResult(3, "c1", "ok"), Text(4, "second")]);
        var facade = CreateFacade(dispatcher, new RecordingThreadService());

        var chunks = await CollectAsync(facade.AttachStreamingAsync(dispatcher.RunId, fromSequence: 2));

        chunks[0].EventType.ShouldBe(RunStreamEventTypes.RunStarted);
        chunks.Where(c => c.Sequence.HasValue).Select(c => c.Sequence!.Value).ShouldBe([3, 4]);
        chunks.Single(c => c.ToolCalls != null).ToolCalls!.Single().Name.ShouldBe("Read",
            "the result is matched to a call the client received before it reconnected");
        chunks.Single(c => c.Text != null).Text.ShouldBe("\n\nsecond",
            "the block separator after the tool call depends on what came before the reconnect");
        chunks[^1].FinishReason.ShouldBe(FinishReasons.Stop);
    }

    [Fact]
    public async Task AttachStreamingAsync_RunTheCallerCannotSee_IsNotFound()
    {
        var dispatcher = new ScriptedDispatcher([]) { Visible = false };
        var facade = CreateFacade(dispatcher, new RecordingThreadService());

        var error = await Should.ThrowAsync<BusinessException>(() => CollectAsync(facade.AttachStreamingAsync(Guid.NewGuid())));
        error.HttpStatusCode.ShouldBe(404);
        error.Code.ShouldBe(ErrorCodes.ChatRunNotFound);
    }

    [Fact]
    public async Task CancelRunAsync_CancelsOnTheServer_AndAnUnknownRunIsNotFound()
    {
        var dispatcher = new ScriptedDispatcher([]);
        var facade = CreateFacade(dispatcher, new RecordingThreadService());

        (await facade.CancelRunAsync(dispatcher.RunId)).Succeeded.ShouldBeTrue();
        dispatcher.Cancelled.ShouldBe([dispatcher.RunId]);

        var unknown = await facade.CancelRunAsync(Guid.NewGuid());
        unknown.Code.ShouldBe(404);
        unknown.ErrorCode.ShouldBe(ErrorCodes.ChatRunNotFound);
    }

    // ---------------------------------------------------------------- permission requests

    /// <summary>权限请求与结局不论粒度都交给客户端：界面据此弹出与收起批准卡片。</summary>
    [Fact]
    public async Task RunStreamingAsync_PermissionRequestAndOutcome_ReachTheClientAsTheirOwnEvents()
    {
        var requestId = Guid.NewGuid();
        var input = new Dictionary<string, object?> { ["command"] = "rm -rf build" };
        var dispatcher = new ScriptedDispatcher(
        [
            ToolUse(1, "toolu_1", "Bash"),
            new CliAgentEvent
            {
                Type = CliAgentEventType.PermissionRequest, Sequence = 2, Tool = "Bash", CallId = "toolu_1", Input = input,
                Content = "Remove the build directory",
                Permission = new CliAgentPermission
                {
                    RequestId = requestId,
                    Options = [new CliPermissionOption("allow_once", "Allow once", CliPermissionOptionKinds.AllowOnce)],
                    ExpiresAt = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc)
                }
            },
            new CliAgentEvent
            {
                Type = CliAgentEventType.PermissionResolved, Sequence = 3, Tool = "Bash", CallId = "toolu_1",
                Permission = new CliAgentPermission { RequestId = requestId, Status = CliPermissionStatus.Allowed, OptionId = "allow_once" }
            }
        ]);
        var facade = CreateFacade(dispatcher, new RecordingThreadService());

        var chunks = await CollectAsync(facade.RunStreamingAsync(Request("hi", ExistingThreadId)));

        var request = chunks.Single(c => c.EventType == RunStreamEventTypes.PermissionRequest);
        request.Sequence.ShouldBe(2);
        request.EventData!["requestId"].ShouldBe(requestId);
        request.EventData["status"].ShouldBe("Pending");
        request.EventData["tool"].ShouldBe("Bash");
        request.EventData["toolCallId"].ShouldBe("toolu_1");
        request.EventData["description"].ShouldBe("Remove the build directory");
        request.EventData["input"].ShouldBe(input);
        ((IReadOnlyList<CliPermissionOption>)request.EventData["options"]).ShouldHaveSingleItem().Id.ShouldBe("allow_once");
        request.EventData.ShouldContainKey("expiresAt");

        var resolved = chunks.Single(c => c.EventType == RunStreamEventTypes.PermissionResolved);
        resolved.EventData!["requestId"].ShouldBe(requestId);
        resolved.EventData["status"].ShouldBe("Allowed");
        resolved.EventData["optionId"].ShouldBe("allow_once");
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task ATurnWithAUserIsAttended_UnlessAnInProcessCallerStartedIt(bool inProcess, bool attended)
    {
        var dispatcher = new ScriptedDispatcher([]);
        var facade = CreateFacade(dispatcher, new RecordingThreadService());
        var request = new AgentRunRequest { AgentId = AgentId, ThreadId = ExistingThreadId, UserMessage = "hi", UserId = UserId, TrustedToolSelection = inProcess };

        await CollectAsync(facade.RunStreamingAsync(request));

        dispatcher.Enqueued.ShouldHaveSingleItem().Attended.ShouldBe(attended);
    }

    [Fact]
    public async Task AnswerRunPermissionAsync_ReachesTheDispatcher_AndAnUnknownRunIsNotFound()
    {
        var dispatcher = new ScriptedDispatcher([]);
        var facade = CreateFacade(dispatcher, new RecordingThreadService());
        var requestId = Guid.NewGuid();

        (await facade.AnswerRunPermissionAsync(dispatcher.RunId, requestId, new CliPermissionAnswerDto { OptionId = "allow_once" }))
            .Succeeded.ShouldBeTrue();
        dispatcher.Answered.ShouldBe([(dispatcher.RunId, requestId, "allow_once")]);

        var unknown = await facade.AnswerRunPermissionAsync(Guid.NewGuid(), requestId, new CliPermissionAnswerDto { OptionId = "allow_once" });
        unknown.Code.ShouldBe(404);
        unknown.ErrorCode.ShouldBe(ErrorCodes.ChatRunNotFound);
    }

    [Fact]
    public async Task GetActiveRunsAsync_AsksForTheThreadsUnfinishedRuns()
    {
        var dispatcher = new ScriptedDispatcher([]);
        var facade = CreateFacade(dispatcher, new RecordingThreadService());

        var runs = await facade.GetActiveRunsAsync(ExistingThreadId);

        runs.ShouldHaveSingleItem().RunId.ShouldBe(dispatcher.RunId);
        dispatcher.LastQuery!.ThreadId.ShouldBe(ExistingThreadId);
        dispatcher.LastQuery.ActiveOnly.ShouldBeTrue();
    }

    // ---------------------------------------------------------------- attachments

    [Fact]
    public async Task Attachments_GoToTheRunAsParts_AndTheTextBecomesThePrompt()
    {
        var dispatcher = new ScriptedDispatcher([]);
        var facade = CreateFacade(dispatcher, new RecordingThreadService());

        await CollectAsync(facade.RunStreamingAsync(Request(null, ExistingThreadId,
        [
            new TextContentPartDto { Text = "what is in this picture?" },
            new ImageContentPartDto { Base64Data = Convert.ToBase64String(Png), MediaType = "image/png", FileName = "a.png" },
            new DocumentContentPartDto { Base64Data = Convert.ToBase64String(Pdf), FileName = "b.pdf" }
        ])));

        var enqueued = dispatcher.Enqueued.ShouldHaveSingleItem();
        enqueued.Prompt.ShouldBe("what is in this picture?");
        enqueued.Attachments!.Select(p => p.GetType()).ShouldBe([typeof(ImageContentPartDto), typeof(DocumentContentPartDto)]);
        enqueued.UserMessageId.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("image/bmp", 415, "AI_UNSUPPORTED_MEDIA_TYPE")]
    [InlineData("too-large", 413, "AI_ATTACHMENT_TOO_LARGE")]
    public async Task Attachments_ThatCannotBeSent_AreRefusedBeforeAnythingIsWritten(string kind, int status, string code)
    {
        var threads = new RecordingThreadService();
        var dispatcher = new ScriptedDispatcher([]);
        var facade = CreateFacade(dispatcher, threads, maxAttachmentBytes: 64);

        var bytes = kind == "too-large" ? Png.Concat(new byte[100]).ToArray() : [0x42, 0x4D, 0x00, 0x00];
        var error = await Should.ThrowAsync<BusinessException>(() => CollectAsync(facade.RunStreamingAsync(Request("look", ExistingThreadId,
        [
            new TextContentPartDto { Text = "look" },
            new ImageContentPartDto { Base64Data = Convert.ToBase64String(bytes), MediaType = "image/png" }
        ]))));

        error.HttpStatusCode.ShouldBe(status);
        error.Code.ShouldBe(code);
        threads.Saved.ShouldBeEmpty("a refused turn leaves no question without an answer behind");
        dispatcher.Enqueued.ShouldBeEmpty();
    }

    [Fact]
    public async Task Attachments_ToARuntimeThatCannotReceiveThem_AreRefused_NotDroppedSilently()
    {
        var threads = new RecordingThreadService();
        var dispatcher = new ScriptedDispatcher([]);
        var facade = CreateFacade(dispatcher, threads, acceptsAttachments: false);

        var error = await Should.ThrowAsync<BusinessException>(() => CollectAsync(facade.RunStreamingAsync(Request("look", ExistingThreadId,
        [
            new TextContentPartDto { Text = "look" },
            new ImageContentPartDto { Base64Data = Convert.ToBase64String(Png) }
        ]))));

        error.HttpStatusCode.ShouldBe(415);
        error.Code.ShouldBe(ErrorCodes.UnsupportedMediaType);
        threads.Saved.ShouldBeEmpty();
        dispatcher.Enqueued.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------- turn handlers

    [Fact]
    public async Task TurnHandlers_ContextIsAppendedToTheRun_InRegistrationOrder()
    {
        var dispatcher = new ScriptedDispatcher([]);
        var first = new RecordingHandler(CliTurnDecision.Proceed("Current time: 10:00"));
        var second = new RecordingHandler(CliTurnDecision.Proceed("Computers online: dev-7"));
        var facade = CreateFacade(dispatcher, new RecordingThreadService(), handlers: [first, second]);

        await CollectAsync(facade.RunStreamingAsync(Request("hi", ExistingThreadId)));

        dispatcher.Enqueued.Single().PerTurnContext.ShouldBe("Current time: 10:00\n\nComputers online: dev-7");
        var seen = first.Turns.ShouldHaveSingleItem();
        seen.ThreadId.ShouldBe(ExistingThreadId);
        seen.UserId.ShouldBe(UserId);
        seen.Message.ShouldBe("hi");
    }

    [Fact]
    public async Task TurnHandlers_ARefusal_ReachesTheCallerWithItsCode_AndNothingIsWrittenOrQueued()
    {
        var threads = new RecordingThreadService();
        var dispatcher = new ScriptedDispatcher([]);
        var refusing = new RecordingHandler(CliTurnDecision.Refuse("This session can only use its own conversation.", "APP_SESSION_RESTRICTED", 403));
        var later = new RecordingHandler(CliTurnDecision.Proceed("never"));
        var facade = CreateFacade(dispatcher, threads, handlers: [refusing, later]);

        var error = await Should.ThrowAsync<BusinessException>(() => CollectAsync(facade.RunStreamingAsync(Request("hi", ExistingThreadId))));

        error.HttpStatusCode.ShouldBe(403);
        error.Code.ShouldBe("APP_SESSION_RESTRICTED");
        error.Message.ShouldBe("This session can only use its own conversation.");
        threads.Saved.ShouldBeEmpty();
        dispatcher.Enqueued.ShouldBeEmpty();
        later.Turns.ShouldBeEmpty("the first refusal decides");
    }

    [Fact]
    public async Task TurnHandlers_AHandlerThatThrows_FailsTheTurnClosed()
    {
        var dispatcher = new ScriptedDispatcher([]);
        var facade = CreateFacade(dispatcher, new RecordingThreadService(), handlers: [new ThrowingHandler()]);

        var error = await Should.ThrowAsync<BusinessException>(() => CollectAsync(facade.RunStreamingAsync(Request("hi", ExistingThreadId))));

        error.Code.ShouldBe(ErrorCodes.CliTurnHandlerFailed);
        dispatcher.Enqueued.ShouldBeEmpty("a broken handler must not let the turn run without its context or past its refusal");
    }

    [Fact]
    public async Task TurnHandlers_OnANewThread_SeeNoThreadBefore_AndTheNewThreadOnceQueued()
    {
        var threads = new RecordingThreadService();
        var dispatcher = new ScriptedDispatcher([]);
        var handler = new RecordingHandler(CliTurnDecision.Proceed());
        var facade = CreateFacade(dispatcher, threads, handlers: [handler]);

        await CollectAsync(facade.RunStreamingAsync(Request("hi", threadId: null)));

        handler.Turns.Single().ThreadId.ShouldBeNull("the thread does not exist until the turn is allowed");
        var queued = handler.Queued.ShouldHaveSingleItem();
        queued.IsNewThread.ShouldBeTrue();
        queued.ThreadId.ShouldBe(threads.CreatedThreadId);
        queued.RunId.ShouldBe(dispatcher.RunId);
        queued.UserMessageId.ShouldBe(threads.Saved.Single().Id);
        queued.UserId.ShouldBe(UserId);
    }

    [Fact]
    public async Task TurnHandlers_ThatFailAfterTheRunIsQueued_DoNotFailTheTurn()
    {
        var dispatcher = new ScriptedDispatcher([Text(1, "ok")]);
        var facade = CreateFacade(dispatcher, new RecordingThreadService(), handlers: [new ThrowingHandler(onQueued: true)]);

        var chunks = await CollectAsync(facade.RunStreamingAsync(Request("hi", ExistingThreadId)));

        chunks[^1].FinishReason.ShouldBe(FinishReasons.Stop);
    }

    [Fact]
    public async Task RewoundTurns_IsPassedToTheHandlers()
    {
        var handler = new RecordingHandler(CliTurnDecision.Proceed());
        var facade = CreateFacade(new ScriptedDispatcher([]), new RecordingThreadService(), handlers: [handler]);

        await CollectAsync(facade.RunStreamingAsync(new AgentRunRequest
        {
            AgentId = AgentId, ThreadId = ExistingThreadId, UserMessage = "again", UserId = UserId, RewoundTurns = true
        }));

        handler.Turns.Single().RewoundTurns.ShouldBeTrue();
    }

    // ---------------------------------------------------------------- helpers

    private static readonly Guid UserId = Guid.NewGuid();

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF");

    private static AgentRunRequest Request(string? message, Guid? threadId, List<ContentPartDto>? content = null) => new()
    {
        AgentId = AgentId,
        ThreadId = threadId,
        UserMessage = message,
        ContentParts = content,
        UserId = UserId
    };

    private static CliAgentEvent Text(int sequence, string text)
        => new() { Type = CliAgentEventType.Text, Content = text, Sequence = sequence };

    private static CliAgentEvent ToolUse(int sequence, string callId, string tool)
        => new() { Type = CliAgentEventType.ToolUse, CallId = callId, Tool = tool, Sequence = sequence };

    private static CliAgentEvent ToolResult(int sequence, string callId, string output)
        => new() { Type = CliAgentEventType.ToolResult, CallId = callId, Output = output, Status = "completed", Sequence = sequence };

    private static async Task<List<AgentStreamChunk>> CollectAsync(IAsyncEnumerable<AgentStreamChunk> stream)
    {
        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in stream)
        {
            chunks.Add(chunk);
        }

        return chunks;
    }

    private static AgentDispatchFacade CreateFacade(
        ScriptedDispatcher dispatcher, RecordingThreadService threads,
        bool acceptsAttachments = true, long maxAttachmentBytes = 10 * 1024 * 1024,
        IReadOnlyList<ICliAgentTurnHandler>? handlers = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<ICliAgentDispatcher>(_ => dispatcher);
        services.AddScoped<IAgentThreadInternalService>(_ => threads);
        foreach (var handler in handlers ?? [])
        {
            services.AddScoped(_ => handler);
        }

        var options = new AiChatOptions { Attachments = new ChatAttachmentOptions { MaxBytes = maxAttachmentBytes } };

        return new AgentDispatchFacade(
            Mock.Of<IAgentRuntime>(MockBehavior.Strict),
            new BoundBindingService(acceptsAttachments),
            dispatcher,
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentDispatchFacade>.Instance,
            new StaticOptionsMonitor<AiChatOptions>(options));
    }

    private sealed record SavedMessage(Guid Id, string Role, string Content);

    private sealed class RecordingThreadService : IAgentThreadInternalService
    {
        public List<SavedMessage> Saved { get; } = [];

        public Guid CreatedThreadId { get; } = Guid.NewGuid();

        public Task<(Guid threadId, bool isNewThread)> GetOrCreateThreadAsync(Guid? threadId, Guid? agentId, CancellationToken ct = default)
            => Task.FromResult(threadId is { } id ? (id, false) : (CreatedThreadId, true));

        public Task<Guid> SaveMessageAsync(Guid threadId, string role, string content,
            string? toolCalls = null, string? usage = null, Guid? messageId = null, CancellationToken ct = default)
        {
            var id = messageId ?? Guid.NewGuid();
            Saved.Add(new SavedMessage(id, role, content));
            return Task.FromResult(id);
        }

        public Task<List<ChatMessage>> GetMessageHistoryAsync(Guid threadId, int? limit = null, CancellationToken ct = default)
            => Task.FromResult(new List<ChatMessage>());

        public Task<string?> GetMetadataValueAsync(Guid threadId, string key, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task SetMetadataValueAsync(Guid threadId, string key, string? valueJson, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    /// <summary>Enqueues one run and replays a fixed event script for it.</summary>
    private sealed class ScriptedDispatcher(IReadOnlyList<CliAgentEvent> events) : ICliAgentDispatcher
    {
        public Guid RunId { get; } = Guid.NewGuid();

        public Guid? AssistantMessageId { get; init; }

        public bool Visible { get; init; } = true;

        public List<CliRunRequestDto> Enqueued { get; } = [];

        public List<Guid> Cancelled { get; } = [];

        public List<(Guid RunId, Guid RequestId, string OptionId)> Answered { get; } = [];

        public Task<Result> AnswerPermissionAsync(Guid runId, Guid requestId, CliPermissionAnswerDto answer, CancellationToken cancellationToken = default)
        {
            if (runId != RunId)
            {
                return Task.FromResult(Result.Failure("CLI run not found.", 404, ErrorCodes.CliRunNotFound));
            }

            Answered.Add((runId, requestId, answer.OptionId));
            return Task.FromResult(Result.Success());
        }

        public CliRunQueryDto? LastQuery { get; private set; }

        public Task<Result<Guid>> EnqueueAsync(CliRunRequestDto request, CancellationToken cancellationToken = default)
        {
            Enqueued.Add(request);
            return Task.FromResult(Result<Guid>.Success(RunId));
        }

        public async IAsyncEnumerable<CliAgentEvent> StreamAsync(Guid runId, int fromSequence = 0,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var evt in events.Where(e => (e.Sequence ?? 0) > fromSequence))
            {
                await Task.Yield();
                yield return evt;
            }
        }

        public Task<Result<CliRunDto>> GetAsync(Guid runId, CancellationToken cancellationToken = default)
            => Task.FromResult(Visible && runId == RunId
                ? Result<CliRunDto>.Success(new CliRunDto
                {
                    Id = runId,
                    ThreadId = ExistingThreadId,
                    Status = CliRunStatus.Completed,
                    Output = "done",
                    AssistantMessageId = AssistantMessageId
                })
                : Result<CliRunDto>.Failure("CLI run not found.", 404, ErrorCodes.CliRunNotFound));

        public Task<Result<IPagedList<CliRunDto>>> GetListAsync(CliRunQueryDto query, CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            IPagedList<CliRunDto> page = new PagedList<CliRunDto>(
                [new CliRunDto { Id = RunId, ThreadId = query.ThreadId, Status = CliRunStatus.Running }], 1, 1, 20);
            return Task.FromResult(Result<IPagedList<CliRunDto>>.Success(page));
        }

        public Task<Result<List<CliRunMessageDto>>> GetMessagesAsync(Guid runId, int fromSequence = 0, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result> CancelAsync(Guid runId, CancellationToken cancellationToken = default)
        {
            if (runId != RunId)
            {
                return Task.FromResult(Result.Failure("CLI run not found.", 404, ErrorCodes.CliRunNotFound));
            }

            Cancelled.Add(runId);
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class BoundBindingService(bool acceptsAttachments) : ICliAgentBindingService
    {
        public Task<CliAgentBindingDto?> GetByAgentIdAsync(Guid agentId, CancellationToken cancellationToken = default)
            => Task.FromResult<CliAgentBindingDto?>(new CliAgentBindingDto
            {
                Id = Guid.NewGuid(), AgentId = agentId, CliRuntimeId = Guid.NewGuid(), ProviderKey = "claude",
                AcceptsAttachments = acceptsAttachments
            });

        public Task<Result<CliAgentBindingDto>> UpsertAsync(Guid agentId, UpsertCliAgentBindingDto input, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result> DeleteAsync(Guid agentId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class RecordingHandler(CliTurnDecision decision) : ICliAgentTurnHandler
    {
        public List<CliTurnContext> Turns { get; } = [];

        public List<CliTurnQueued> Queued { get; } = [];

        public Task<CliTurnDecision> OnTurnAsync(CliTurnContext turn, CancellationToken cancellationToken = default)
        {
            Turns.Add(turn);
            return Task.FromResult(decision);
        }

        public Task OnTurnQueuedAsync(CliTurnQueued queued, CancellationToken cancellationToken = default)
        {
            Queued.Add(queued);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHandler(bool onQueued = false) : ICliAgentTurnHandler
    {
        public Task<CliTurnDecision> OnTurnAsync(CliTurnContext turn, CancellationToken cancellationToken = default)
            => onQueued ? Task.FromResult(CliTurnDecision.Proceed()) : throw new InvalidOperationException("handler broke");

        public Task OnTurnQueuedAsync(CliTurnQueued queued, CancellationToken cancellationToken = default)
            => onQueued ? throw new InvalidOperationException("handler broke") : Task.CompletedTask;
    }
}
