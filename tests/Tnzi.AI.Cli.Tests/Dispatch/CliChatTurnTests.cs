using Tnzi.AI.Engine;
using Tnzi.AI.Entities.Configs;
using Tnzi.Exceptions;

namespace Tnzi.AI.Cli.Tests;

public class CliChatDbContext : TnziDbContext<CliChatDbContext>
{
    public CliChatDbContext(DbContextOptions<CliChatDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new CliRunConfiguration());
        modelBuilder.ApplyConfiguration(new CliRunMessageConfiguration());
        modelBuilder.ApplyConfiguration(new CliRunPermissionRequestConfiguration());
        modelBuilder.ApplyConfiguration(new CliAgentBindingConfiguration());
        modelBuilder.ApplyConfiguration(new CliRuntimeConfiguration());
        modelBuilder.ApplyConfiguration(new AgentConfiguration());
        modelBuilder.ApplyConfiguration(new ProviderConfiguration());
        modelBuilder.ApplyConfiguration(new AgentThreadConfiguration());
        modelBuilder.ApplyConfiguration(new AgentThreadMessageConfiguration());
        modelBuilder.ApplyConfiguration(new AgentAttachmentConfiguration());
        modelBuilder.ApplyConfiguration(new QueuedThreadMessageConfiguration());
        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

/// <summary>
/// A chat turn on an external CLI runtime, end to end through the real dispatcher, executor and thread service:
/// the reply and its tool calls land in the thread when the run ends, tool images become attachments, uploads reach the
/// CLI, edits and regenerations cut the CLI session back, and a consumer's per-turn context reaches the prompt.
/// </summary>
/// <remarks>
/// Only the process and the protocol are fake (a scripted adapter); everything that decides what is stored and what the
/// CLI is told runs for real on SQLite.
/// </remarks>
public class CliChatTurnTests : CliChatTurnTestBase
{
    // ---------------------------------------------------------------- the reply is written by the run

    [Fact]
    public async Task Completed_TheReplyAndItsToolCalls_AreInTheThreadBeforeTheRunEnds()
    {
        var (threadId, userMessageId) = await SeedThreadWithQuestionAsync("what is on my screen?");
        var run = await SeedClaimedRunAsync(threadId, userMessageId);
        var adapter = new ScriptedAdapter(
        [
            new CliAgentEvent { Type = CliAgentEventType.Text, Content = "Let me look." },
            new CliAgentEvent
            {
                Type = CliAgentEventType.ToolUse, CallId = "call-1", Tool = "mcp__tnzi__screenshot",
                Input = new Dictionary<string, object?> { ["device"] = "dev-7" }
            },
            new CliAgentEvent
            {
                Type = CliAgentEventType.ToolResult, CallId = "call-1", Status = "completed", Output = "captured\n[image: image/png]",
                OutputBlocks =
                [
                    new CliContentBlock { Type = CliContentBlock.TextType, Text = "captured" },
                    new CliContentBlock { Type = CliContentBlock.ImageType, MediaType = "image/png", Data = Convert.ToBase64String(Png) }
                ]
            },
            new CliAgentEvent { Type = CliAgentEventType.Text, Content = "It is a login page." }
        ]);

        await CreateExecutor(adapter).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var persisted = await ReloadRunAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Completed);

        var reply = await DbContext.Set<AgentThreadMessage>().AsNoTracking()
            .SingleAsync(m => m.ThreadId == threadId && m.Role == MessageRole.Assistant);
        reply.Content.ShouldBe("Let me look.\n\nIt is a login page.");
        reply.Status.ShouldBeNull("a completed reply carries no status");
        persisted.AssistantMessageId.ShouldBe(reply.Id, "the stream's terminal chunk reads the reply id from the run");
        Finished.ShouldHaveSingleItem().AssistantMessageId.ShouldBe(reply.Id);

        var calls = JsonSerializer.Deserialize<List<ToolCallDetail>>(reply.ToolCalls!, TnziJsonDefaults.Options)!;
        var call = calls.ShouldHaveSingleItem();
        call.Id.ShouldBe("call-1");
        call.Name.ShouldBe("mcp__tnzi__screenshot");
        call.Status.ShouldBe(ToolCallStatuses.Completed);
        call.Input!.Value.GetProperty("device").GetString().ShouldBe("dev-7");
        call.TextOffset.ShouldBe("Let me look.".Length);
        var image = call.Result!.Single(b => b.Type == "image");

        var stored = await DbContext.Set<AgentAttachment>().AsNoTracking().SingleAsync();
        image.AttachmentId.ShouldBe(stored.Id);
        stored.Origin.ShouldBe(AttachmentOrigin.ToolResult);
        stored.MessageId.ShouldBe(reply.Id, "the tool image is shown on the reply it belongs to");
        stored.OwnerId.ShouldBe(TestHelper.DefaultTestUserId);
        stored.ToolCallId.ShouldBe("call-1");
        stored.Content.ShouldBe(Png);

        var resultRow = await DbContext.Set<CliRunMessage>().AsNoTracking().IgnoreQueryFilters()
            .SingleAsync(m => m.RunId == run.Id && m.Type == CliAgentEventType.ToolResult);
        resultRow.Tool.ShouldBe("mcp__tnzi__screenshot", "the result row names its tool, so a reconnecting client can show it");
        resultRow.OutputBlocksJson.ShouldNotBeNull();
        resultRow.OutputBlocksJson.ShouldNotContain(Convert.ToBase64String(Png), Case.Sensitive,
            "image bytes live in the attachment, not in the event log");
    }

    // ---------------------------------------------------------------- turns after background tasks, compaction

    /// <summary>
    /// The CLI started a background task and answered; when the task finished it started another turn by itself. Each
    /// answer is its own reply in the thread, in order: the first one is written as soon as its turn ends (the boundary
    /// event carries its id), the last one when the run ends, and the run points at the last one.
    /// </summary>
    [Fact]
    public async Task ATurnAfterABackgroundTask_IsItsOwnReply_AndTheBoundaryCarriesTheFirstOnesId()
    {
        var (threadId, userMessageId) = await SeedThreadWithQuestionAsync("run the tests in the background");
        var run = await SeedClaimedRunAsync(threadId, userMessageId);
        var adapter = new ScriptedAdapter(
        [
            new CliAgentEvent { Type = CliAgentEventType.ToolUse, CallId = "call-1", Tool = "Bash", Input = new Dictionary<string, object?> { ["command"] = "dotnet test" } },
            new CliAgentEvent { Type = CliAgentEventType.ToolResult, CallId = "call-1", Status = "completed", Output = "Command running in background with ID: b1." },
            new CliAgentEvent { Type = CliAgentEventType.Text, Content = "The tests are running." },
            new CliAgentEvent
            {
                Type = CliAgentEventType.TurnCompleted, Content = "The tests are running.",
                Turn = new CliTurnBoundary { BackgroundTasks = [new CliBackgroundTask("b1", "Run the tests", "local_bash")] }
            },
            new CliAgentEvent { Type = CliAgentEventType.Status, Status = "background_task_finished", Content = "Background command \"Run the tests\" completed (exit code 0)" },
            new CliAgentEvent { Type = CliAgentEventType.Text, Content = "All 12 tests passed." }
        ], output: "All 12 tests passed.");

        await CreateExecutor(adapter).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var replies = await DbContext.Set<AgentThreadMessage>().AsNoTracking()
            .Where(m => m.ThreadId == threadId && m.Role == MessageRole.Assistant)
            .OrderBy(m => m.Order).ToListAsync();
        replies.Select(m => m.Content).ShouldBe(["The tests are running.", "All 12 tests passed."]);
        replies[0].ToolCalls.ShouldNotBeNull("the first turn's tool call stays with the first answer");
        replies[1].ToolCalls.ShouldBeNull("the second answer starts after the boundary");

        var boundary = await DbContext.Set<CliRunMessage>().AsNoTracking().IgnoreQueryFilters()
            .SingleAsync(m => m.RunId == run.Id && m.Type == CliAgentEventType.TurnCompleted);
        var turn = CliRunMessageConverter.ToEvent(boundary).Turn.ShouldNotBeNull();
        turn.AssistantMessageId.ShouldBe(replies[0].Id);
        turn.BackgroundTasks.ShouldHaveSingleItem().Description.ShouldBe("Run the tests");

        var persisted = await ReloadRunAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Completed);
        persisted.AssistantMessageId.ShouldBe(replies[1].Id);
        persisted.Output.ShouldBe("All 12 tests passed.");
    }

    /// <summary>
    /// The wait for a background task ran out before the CLI's next turn: the run's deliverable is still the first answer,
    /// which is already in the thread. It is not written a second time, and the run points at it.
    /// </summary>
    [Fact]
    public async Task WhenTheWaitForABackgroundTaskRunsOut_TheFirstAnswerIsNotWrittenTwice()
    {
        var (threadId, userMessageId) = await SeedThreadWithQuestionAsync("start the build");
        var run = await SeedClaimedRunAsync(threadId, userMessageId);
        var adapter = new ScriptedAdapter(
        [
            new CliAgentEvent { Type = CliAgentEventType.Text, Content = "Started." },
            new CliAgentEvent
            {
                Type = CliAgentEventType.TurnCompleted, Content = "Started.",
                Turn = new CliTurnBoundary { BackgroundTasks = [new CliBackgroundTask("b1", "Build", "local_bash")] }
            },
            new CliAgentEvent { Type = CliAgentEventType.Status, Status = "background_tasks_stopped", Content = "Build" }
        ], output: "Started.");

        await CreateExecutor(adapter).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var reply = await DbContext.Set<AgentThreadMessage>().AsNoTracking()
            .SingleAsync(m => m.ThreadId == threadId && m.Role == MessageRole.Assistant);
        reply.Content.ShouldBe("Started.");
        (await ReloadRunAsync(run.Id)).AssistantMessageId.ShouldBe(reply.Id);
    }

    /// <summary>
    /// A turn that only compacted the conversation (<c>/compact</c>) has no answer: the run has no output (it does not
    /// claim a missing final response), and the thread gets an empty reply with status <c>compacted</c>.
    /// </summary>
    [Fact]
    public async Task ACompactOnlyTurn_IsStoredAsACompactedReply_WithoutClaimingAMissingAnswer()
    {
        var (threadId, userMessageId) = await SeedThreadWithQuestionAsync("/compact");
        var run = await SeedClaimedRunAsync(threadId, userMessageId, prompt: "/compact");
        var compaction = new CliCompaction("manual", 28750, 3533);
        var adapter = new ScriptedAdapter(
            [new CliAgentEvent { Type = CliAgentEventType.Compacted, Compaction = compaction }], compaction: compaction);

        await CreateExecutor(adapter).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var reply = await DbContext.Set<AgentThreadMessage>().AsNoTracking()
            .SingleAsync(m => m.ThreadId == threadId && m.Role == MessageRole.Assistant);
        reply.Status.ShouldBe(ThreadMessageStatuses.Compacted);
        reply.Content.ShouldBeEmpty();

        var persisted = await ReloadRunAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Completed);
        persisted.Output.ShouldBeNull();
        persisted.AssistantMessageId.ShouldBe(reply.Id);

        var row = await DbContext.Set<CliRunMessage>().AsNoTracking().IgnoreQueryFilters()
            .SingleAsync(m => m.RunId == run.Id && m.Type == CliAgentEventType.Compacted);
        CliRunMessageConverter.ToEvent(row).Compaction.ShouldBe(compaction);
    }

    [Fact]
    public async Task StoppedByTheUser_WhatWasWrittenIsKeptAsAStoppedReply()
    {
        var (threadId, userMessageId) = await SeedThreadWithQuestionAsync("long task");
        var run = await SeedClaimedRunAsync(threadId, userMessageId);
        var adapter = new ScriptedAdapter(
            [new CliAgentEvent { Type = CliAgentEventType.Text, Content = "Working on it" }],
            status: CliRunStatus.Cancelled);

        await CreateExecutor(adapter).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var reply = await DbContext.Set<AgentThreadMessage>().AsNoTracking()
            .SingleAsync(m => m.ThreadId == threadId && m.Role == MessageRole.Assistant);
        reply.Content.ShouldBe("Working on it");
        reply.Status.ShouldBe(ThreadMessageStatuses.Stopped);
        (await ReloadRunAsync(run.Id)).AssistantMessageId.ShouldBe(reply.Id);
    }

    [Fact]
    public async Task Failed_WithNothingWritten_LeavesNoReply()
    {
        var (threadId, userMessageId) = await SeedThreadWithQuestionAsync("hi");
        var run = await SeedClaimedRunAsync(threadId, userMessageId);
        var adapter = new ScriptedAdapter([], status: CliRunStatus.Failed);

        await CreateExecutor(adapter).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        (await DbContext.Set<AgentThreadMessage>().CountAsync(m => m.Role == MessageRole.Assistant)).ShouldBe(0);
        (await ReloadRunAsync(run.Id)).AssistantMessageId.ShouldBeNull();
    }

    /// <summary>
    /// A run that ends because the CLI's account hit its usage limit is its own outcome: the run, the finish event and the
    /// reply in the thread all say so and when the limit resets, and the reply is written even with nothing in it.
    /// </summary>
    [Fact]
    public async Task UsageLimit_IsKeptAsItsOwnReply_AndTheRunAndTheFinishEventSayWhenItResets()
    {
        var resetsAt = new DateTimeOffset(2026, 10, 19, 15, 0, 0, TimeSpan.Zero);
        var (threadId, userMessageId) = await SeedThreadWithQuestionAsync("hi");
        var run = await SeedClaimedRunAsync(threadId, userMessageId);
        var adapter = new ScriptedAdapter([], status: CliRunStatus.Failed,
            failureReason: CliRunFailureReason.UsageLimit, usageLimitResetsAt: resetsAt);

        await CreateExecutor(adapter).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var reply = await DbContext.Set<AgentThreadMessage>().AsNoTracking()
            .SingleAsync(m => m.ThreadId == threadId && m.Role == MessageRole.Assistant);
        reply.Status.ShouldBe(ThreadMessageStatuses.UsageLimit);
        reply.UsageLimitResetsAt.ShouldBe(resetsAt.UtcDateTime);
        reply.Content.ShouldBeEmpty("nothing was written before the limit, but the outcome still shows on reload");

        var stored = await ReloadRunAsync(run.Id);
        stored.FailureReason.ShouldBe(CliRunFailureReason.UsageLimit);
        stored.UsageLimitResetsAt.ShouldBe(resetsAt.UtcDateTime);
        stored.AssistantMessageId.ShouldBe(reply.Id);

        var finished = Finished.ShouldHaveSingleItem();
        finished.FailureReason.ShouldBe(CliRunFailureReason.UsageLimit);
        finished.UsageLimitResetsAt.ShouldBe(resetsAt.UtcDateTime);
    }

    [Fact]
    public async Task ThroughTheFacade_AUsageLimit_EndsTheStreamWithItsOwnCodeAndTheResetTime()
    {
        await SeedBindingAsync();
        var resetsAt = new DateTimeOffset(2026, 10, 19, 15, 0, 0, TimeSpan.Zero);
        var adapter = new ScriptedAdapter([], status: CliRunStatus.Failed,
            failureReason: CliRunFailureReason.UsageLimit, usageLimitResetsAt: resetsAt);
        var facade = CreateFacade(new FixedContextHandler("ctx"));

        var chunks = await StreamThroughAsync(facade, adapter, new AgentRunRequest
        {
            AgentId = AgentId, UserId = TestHelper.DefaultTestUserId, UserMessage = "hi"
        });

        var terminal = chunks[^1];
        terminal.FinishReason.ShouldBe(FinishReasons.Error);
        terminal.ErrorCode.ShouldBe(Tnzi.AI.Metadata.ErrorCodes.CliUsageLimit);
        terminal.UsageLimitResetsAt.ShouldBe(resetsAt.UtcDateTime);
    }

    /// <summary>
    /// The consumer's per-turn metadata reaches its turn handler and is stored with the user message.
    /// </summary>
    [Fact]
    public async Task ThroughTheFacade_TurnMetadata_ReachesTheHandler_AndIsStoredWithTheMessage()
    {
        await SeedBindingAsync();
        var handler = new RecordingHandler();
        var facade = CreateFacade(handler);
        using var targets = JsonDocument.Parse("""["pc-1","pc-2"]""");

        await StreamThroughAsync(facade, new ScriptedAdapter([new CliAgentEvent { Type = CliAgentEventType.Text, Content = "ok" }]),
            new AgentRunRequest
            {
                AgentId = AgentId, UserId = TestHelper.DefaultTestUserId, UserMessage = "restart them",
                TurnMetadata = new Dictionary<string, JsonElement> { ["targets"] = targets.RootElement.Clone() }
            });

        var seen = handler.Turns.ShouldHaveSingleItem().Metadata;
        seen["targets"].EnumerateArray().Select(e => e.GetString()).ShouldBe(["pc-1", "pc-2"]);
        handler.Queued.ShouldHaveSingleItem().Metadata.ContainsKey("targets").ShouldBeTrue();

        var question = await DbContext.Set<AgentThreadMessage>().AsNoTracking().SingleAsync(m => m.Role == MessageRole.User);
        question.MetadataJson.ShouldNotBeNull().ShouldContain("pc-2");
    }

    [Fact]
    public async Task ToolImages_BeyondTheLimits_AreReplacedByANote_NotDropped()
    {
        var (threadId, userMessageId) = await SeedThreadWithQuestionAsync("shots");
        var run = await SeedClaimedRunAsync(threadId, userMessageId);
        var tooBig = Png.Concat(new byte[200]).ToArray();
        var adapter = new ScriptedAdapter(
        [
            new CliAgentEvent { Type = CliAgentEventType.ToolUse, CallId = "c1", Tool = "shot" },
            new CliAgentEvent
            {
                Type = CliAgentEventType.ToolResult, CallId = "c1", Status = "completed",
                OutputBlocks =
                [
                    new CliContentBlock { Type = CliContentBlock.ImageType, MediaType = "image/png", Data = Convert.ToBase64String(tooBig) },
                    new CliContentBlock { Type = CliContentBlock.ImageType, MediaType = "image/png", Data = Convert.ToBase64String(Png) },
                    new CliContentBlock { Type = CliContentBlock.ImageType, MediaType = "image/png", Data = Convert.ToBase64String(Png) }
                ]
            }
        ]);

        await CreateExecutor(adapter, new CliToolResultOptions { MaxImageBytes = 100, MaxImagesPerResult = 1 })
            .ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var row = await DbContext.Set<CliRunMessage>().AsNoTracking().IgnoreQueryFilters()
            .SingleAsync(m => m.RunId == run.Id && m.Type == CliAgentEventType.ToolResult);
        var blocks = JsonSerializer.Deserialize<List<CliContentBlock>>(row.OutputBlocksJson!, TnziJsonDefaults.Options)!;
        blocks[0].Type.ShouldBe(CliContentBlock.TextType);
        blocks[0].Text!.ShouldContain("larger than");
        blocks[1].AttachmentId.ShouldNotBeNull();
        blocks[2].Text!.ShouldContain("at most 1");
        (await DbContext.Set<AgentAttachment>().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Uploads_StoredWithTheRun_AreHandedToTheCli()
    {
        var (threadId, userMessageId) = await SeedThreadWithQuestionAsync("describe this");
        var run = await SeedClaimedRunAsync(threadId, userMessageId);
        DbContext.Set<AgentAttachment>().Add(new AgentAttachment
        {
            RunId = run.Id, ThreadId = threadId, MessageId = userMessageId, Origin = AttachmentOrigin.Upload,
            MediaType = "image/png", FileName = "screen.png", Content = Png, Size = Png.Length,
            OwnerId = TestHelper.DefaultTestUserId
        });
        await DbContext.SaveChangesAsync();
        var adapter = new ScriptedAdapter([new CliAgentEvent { Type = CliAgentEventType.Text, Content = "A screen." }]);

        await CreateExecutor(adapter).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var sent = adapter.Contexts.Single().Attachments.ShouldHaveSingleItem();
        sent.MediaType.ShouldBe("image/png");
        sent.FileName.ShouldBe("screen.png");
        sent.Content.ShouldBe(Png);
    }

    [Fact]
    public async Task TheSessionEntryOfARun_IsRecordedForLaterRewinds()
    {
        var (threadId, userMessageId) = await SeedThreadWithQuestionAsync("hi");
        var run = await SeedClaimedRunAsync(threadId, userMessageId);
        var adapter = new ScriptedAdapter([new CliAgentEvent { Type = CliAgentEventType.Text, Content = "hello" }], entryId: "entry-9");

        await CreateExecutor(adapter).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        (await ReloadRunAsync(run.Id)).ProviderSessionEntryId.ShouldBe("entry-9");
    }

    // ---------------------------------------------------------------- edit and regenerate cut the session back

    [Fact]
    public async Task ANormalFollowUp_ResumesTheLastTurnWithoutCutting()
    {
        var (threadId, u1) = await SeedThreadWithQuestionAsync("remember APPLE");
        await SeedFinishedRunAsync(threadId, u1, session: "s1", entry: "e1", completedAt: DateTime.UtcNow.AddMinutes(-5));
        await AddMessageAsync(threadId, MessageRole.Assistant, "ok");
        var u2 = await AddMessageAsync(threadId, MessageRole.User, "what did I say?");
        var run = await SeedClaimedRunAsync(threadId, u2);
        var adapter = new ScriptedAdapter([]);

        await CreateExecutor(adapter).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var context = adapter.Contexts.Single();
        context.ResumeSessionId.ShouldBe("s1");
        context.ResumeAtEntryId.ShouldBeNull();
    }

    [Fact]
    public async Task AfterARegenerate_TheSessionIsCutBackToTheTurnBefore()
    {
        // u1 answered by r1 (entry e1); u2 answered by r2 (entry e2); then u2 and its answer were removed and sent again as u2b
        var (threadId, u1) = await SeedThreadWithQuestionAsync("remember APPLE");
        await SeedFinishedRunAsync(threadId, u1, session: "s1", entry: "e1", completedAt: DateTime.UtcNow.AddMinutes(-10));
        await AddMessageAsync(threadId, MessageRole.Assistant, "ok");
        var removedQuestion = Guid.NewGuid();
        await SeedFinishedRunAsync(threadId, removedQuestion, session: "s1", entry: "e2", completedAt: DateTime.UtcNow.AddMinutes(-5));
        var u2b = await AddMessageAsync(threadId, MessageRole.User, "remember BANANA (again)");
        var run = await SeedClaimedRunAsync(threadId, u2b);
        var adapter = new ScriptedAdapter([new CliAgentEvent { Type = CliAgentEventType.Text, Content = "ok" }], sessionId: "s1-fork");

        await CreateExecutor(adapter).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var context = adapter.Contexts.Single();
        context.ResumeSessionId.ShouldBe("s1");
        context.ResumeAtEntryId.ShouldBe("e1", "the replaced turn must not stay in the CLI session");
        context.PromptWithTurnContext.ShouldNotContain("could not be resumed");

        var persisted = await ReloadRunAsync(run.Id);
        persisted.ResumeAtEntryId.ShouldBe("e1", "a re-executed run must cut at the same place");
        persisted.ProviderSessionId.ShouldBe("s1-fork", "the next turn resumes the forked session");
    }

    [Fact]
    public async Task AfterARewind_OnAProviderThatCannotCut_AFreshSessionIsStartedAndTheAgentIsTold()
    {
        var (threadId, u1) = await SeedThreadWithQuestionAsync("one");
        await SeedFinishedRunAsync(threadId, u1, session: "s1", entry: "e1", completedAt: DateTime.UtcNow.AddMinutes(-10));
        await SeedFinishedRunAsync(threadId, Guid.NewGuid(), session: "s1", entry: "e2", completedAt: DateTime.UtcNow.AddMinutes(-5));
        var u2b = await AddMessageAsync(threadId, MessageRole.User, "two again");
        var run = await SeedClaimedRunAsync(threadId, u2b);
        var adapter = new ScriptedAdapter([]);

        await CreateExecutor(adapter, supportsResumeAt: false).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var context = adapter.Contexts.Single();
        context.ResumeSessionId.ShouldBeNull("resuming would bring the replaced turn back");
        context.PromptWithTurnContext.ShouldContain("could not be resumed");
    }

    [Fact]
    public async Task EditingTheFirstMessage_StartsAFreshSession_WithNothingToApologiseFor()
    {
        var threadId = await SeedThreadAsync();
        await SeedFinishedRunAsync(threadId, Guid.NewGuid(), session: "s1", entry: "e1", completedAt: DateTime.UtcNow.AddMinutes(-5));
        var edited = await AddMessageAsync(threadId, MessageRole.User, "a different first question");
        var run = await SeedClaimedRunAsync(threadId, edited);
        var adapter = new ScriptedAdapter([]);

        await CreateExecutor(adapter).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var context = adapter.Contexts.Single();
        context.ResumeSessionId.ShouldBeNull();
        context.PromptWithTurnContext.ShouldNotContain("could not be resumed");
    }

    // ---------------------------------------------------------------- the whole way through

    /// <summary>
    /// A consumer's per-turn context and an image attachment go from the chat request, through the dispatch facade and the
    /// queue, to what the CLI is given.
    /// </summary>
    [Fact]
    public async Task ThroughTheFacade_TheHandlersContextAndTheImageReachTheCli()
    {
        await SeedBindingAsync();
        var adapter = new ScriptedAdapter([new CliAgentEvent { Type = CliAgentEventType.Text, Content = "A red square." }]);
        var facade = CreateFacade(new FixedContextHandler("Current time: 10:00 UTC"));

        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in facade.RunStreamingAsync(new AgentRunRequest
        {
            AgentId = AgentId,
            UserId = TestHelper.DefaultTestUserId,
            ContentParts =
            [
                new TextContentPartDto { Text = "what is this?" },
                new ImageContentPartDto { Base64Data = Convert.ToBase64String(Png), FileName = "red.png" }
            ]
        }))
        {
            chunks.Add(chunk);
            if (chunk.EventType == RunStreamEventTypes.RunStarted)
            {
                // the queue processor's part: claim and run it while the stream waits
                await ClaimAsync(chunk.RunId!.Value);
                await CreateExecutor(adapter).ExecuteAsync(chunk.RunId.Value, CancellationToken.None).WaitAsync(TestTimeout);
            }
        }

        var context = adapter.Contexts.Single();
        context.Prompt.ShouldContain("what is this?");
        context.PromptWithTurnContext.ShouldContain("Current time: 10:00 UTC", Case.Sensitive, "the handler's context goes with the prompt");
        context.Prompt.ShouldNotContain("Current time", Case.Sensitive, "the context travels separately, so a slash command's arguments stay clean");
        context.TurnContext.ShouldNotBeNull().ShouldContain("Current time: 10:00 UTC");
        context.Attachments.ShouldHaveSingleItem().FileName.ShouldBe("red.png");

        var terminal = chunks[^1];
        terminal.FinishReason.ShouldBe(FinishReasons.Stop);
        terminal.AssistantMessageId.ShouldNotBeNull();

        var thread = await DbContext.Set<AgentThreadMessage>().AsNoTracking().OrderBy(m => m.Order).ToListAsync();
        thread.Select(m => (m.Role, m.Content)).ShouldBe([("user", "what is this?"), ("assistant", "A red square.")]);
        var upload = await DbContext.Set<AgentAttachment>().AsNoTracking().SingleAsync(a => a.Origin == AttachmentOrigin.Upload);
        upload.MessageId.ShouldBe(thread[0].Id, "the attachment is shown with the message it was sent with");
    }

    [Fact]
    public async Task ThroughTheFacade_AnEmailForATurnThatTakesFiles_IsStoredWithItsName_NotSentInline_AndNamedToTheCli()
    {
        await SeedBindingAsync();
        FilePolicy.Accepts = true;
        ProfileContributors.Add(new PlacingContributor(ServiceProvider));
        var adapter = new ScriptedAdapter([new CliAgentEvent { Type = CliAgentEventType.Text, Content = "It is an invoice." }]);
        var eml = "From: a@example.com\r\nSubject: Invoice\r\n\r\nPlease pay."u8.ToArray();

        await StreamThroughAsync(CreateFacade(new FixedContextHandler("Current time: 10:00 UTC")), adapter, new AgentRunRequest
        {
            AgentId = AgentId,
            UserId = TestHelper.DefaultTestUserId,
            ContentParts =
            [
                new TextContentPartDto { Text = "what does this mail want?" },
                new ImageContentPartDto { Base64Data = Convert.ToBase64String(Png), FileName = "red.png" },
                new DocumentContentPartDto { Base64Data = Convert.ToBase64String(eml), MediaType = "message/rfc822", FileName = "invoice.eml" }
            ]
        });

        var stored = await DbContext.Set<AgentAttachment>().AsNoTracking().Where(a => a.Origin == AttachmentOrigin.Upload).ToListAsync();
        var mail = stored.Single(a => a.FileName == "invoice.eml");
        mail.MediaType.ShouldBe("message/rfc822");
        mail.Size.ShouldBe(eml.Length);
        mail.Content.ShouldBe(eml);
        mail.MessageId.ShouldNotBeNull("the file is shown with the message it was sent with");

        var context = adapter.Contexts.Single();
        context.Attachments.ShouldHaveSingleItem().FileName.ShouldBe("red.png", "images still go inline, the file does not");
        var file = context.Files.ShouldHaveSingleItem();
        file.AttachmentId.ShouldBe(mail.Id);
        file.Path.ShouldBe("attachments/invoice.eml");
        var turnContext = context.TurnContext.ShouldNotBeNull();
        turnContext.ShouldContain("Current time: 10:00 UTC");
        turnContext.ShouldContain("invoice.eml");
        turnContext.ShouldContain("at attachments/invoice.eml", Case.Sensitive, "the agent is told where to open it");
        turnContext.ShouldContain("not shown to you inline");
        context.Prompt.ShouldBe("what does this mail want?");

        FilePolicy.Requests.Count.ShouldBe(2, "the facade asks before writing anything, the dispatcher again as it enqueues");
        FilePolicy.Requests.ShouldAllBe(r => r.Files.Count == 1 && r.Files[0].FileName == "invoice.eml" && r.Files[0].IsFile);
        FilePolicy.Requests[1].UserMessageId.ShouldBe(mail.MessageId);
    }

    [Fact]
    public async Task ThroughTheFacade_AFileForATurnThatDoesNotTakeFiles_IsRefusedWith415_AndNothingIsWritten()
    {
        await SeedBindingAsync();
        var facade = CreateFacade(new FixedContextHandler("ctx"));

        var error = await Should.ThrowAsync<BusinessException>(async () =>
        {
            await foreach (var _ in facade.RunStreamingAsync(new AgentRunRequest
            {
                AgentId = AgentId,
                UserId = TestHelper.DefaultTestUserId,
                ContentParts =
                [
                    new TextContentPartDto { Text = "read this" },
                    new DocumentContentPartDto { Base64Data = Convert.ToBase64String("hello"u8.ToArray()), MediaType = "text/plain", FileName = "notes.txt" }
                ]
            }))
            {
            }
        });

        error.HttpStatusCode.ShouldBe(415);
        error.Code.ShouldBe(Tnzi.AI.Metadata.ErrorCodes.UnsupportedMediaType);
        error.Message.ShouldContain("notes.txt");
        (await DbContext.Set<CliRun>().CountAsync()).ShouldBe(0);
        (await DbContext.Set<AgentThreadMessage>().CountAsync()).ShouldBe(0);
        (await DbContext.Set<AgentAttachment>().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task AFileWithoutAPlace_IsStillNamedToTheCli_AsNotReadable()
    {
        var (threadId, userMessageId) = await SeedThreadWithQuestionAsync("read the log");
        var run = await SeedClaimedRunAsync(threadId, userMessageId);
        DbContext.Set<AgentAttachment>().Add(new AgentAttachment
        {
            RunId = run.Id, ThreadId = threadId, MessageId = userMessageId, Origin = AttachmentOrigin.Upload,
            MediaType = "text/plain", FileName = "build.log", Content = [1, 2, 3], Size = 3,
            OwnerId = TestHelper.DefaultTestUserId
        });
        await DbContext.SaveChangesAsync();
        var adapter = new ScriptedAdapter([new CliAgentEvent { Type = CliAgentEventType.Text, Content = "ok" }]);

        await CreateExecutor(adapter).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var context = adapter.Contexts.Single();
        context.Attachments.ShouldBeEmpty();
        context.Files.ShouldHaveSingleItem().Path.ShouldBeNull();
        context.TurnContext.ShouldNotBeNull().ShouldContain("build.log (3 bytes, text/plain): not placed where you can read it");
    }

    [Fact]
    public async Task ThroughTheFacade_AHandlersRefusal_ReachesTheCaller_AndNothingIsQueued()
    {
        await SeedBindingAsync();
        var facade = CreateFacade(new RefusingHandler());

        var error = await Should.ThrowAsync<BusinessException>(async () =>
        {
            await foreach (var _ in facade.RunStreamingAsync(new AgentRunRequest
            {
                AgentId = AgentId, UserId = TestHelper.DefaultTestUserId, UserMessage = "hi"
            }))
            {
            }
        });

        error.Code.ShouldBe("APP_RESTRICTED");
        error.HttpStatusCode.ShouldBe(403);
        (await DbContext.Set<CliRun>().CountAsync()).ShouldBe(0);
        (await DbContext.Set<AgentThreadMessage>().CountAsync()).ShouldBe(0);
    }

    /// <summary>What a consumer does: places the run's uploads under <c>attachments/</c> and says where.</summary>
    private sealed class PlacingContributor(IServiceProvider services) : ICliLaunchProfileContributor
    {
        public async Task<CliLaunchProfile> ContributeAsync(CliLaunchProfileRequest request, CliLaunchProfile profile, CancellationToken cancellationToken)
        {
            using var scope = services.CreateScope();
            var uploads = await scope.ServiceProvider.GetRequiredService<IAgentAttachmentService>()
                .GetForRunAsync(request.Scope.RunId!.Value, AttachmentOrigin.Upload, cancellationToken);
            return profile with { AttachmentPaths = uploads.ToDictionary(a => a.Id, a => $"attachments/{a.FileName}") };
        }
    }
}
