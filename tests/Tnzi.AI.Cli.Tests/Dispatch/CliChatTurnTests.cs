using System.Runtime.CompilerServices;
using Tnzi.AI.Engine;
using Tnzi.AI.Entities.Configs;
using Tnzi.EventBus;
using Tnzi.Exceptions;
using Tnzi.Json;

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
        modelBuilder.ApplyConfiguration(new CliAgentBindingConfiguration());
        modelBuilder.ApplyConfiguration(new CliRuntimeConfiguration());
        modelBuilder.ApplyConfiguration(new AgentConfiguration());
        modelBuilder.ApplyConfiguration(new ProviderConfiguration());
        modelBuilder.ApplyConfiguration(new AgentThreadConfiguration());
        modelBuilder.ApplyConfiguration(new AgentThreadMessageConfiguration());
        modelBuilder.ApplyConfiguration(new AgentAttachmentConfiguration());
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
public class CliChatTurnTests : IntegratedTestBase<CliChatDbContext>
{
    private static readonly Guid AgentId = Guid.Parse("11111111-1111-1111-1111-1111111111c1");
    private static readonly Guid RuntimeId = Guid.Parse("22222222-2222-2222-2222-2222222222c2");
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);

    private static readonly byte[] Png =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53, 0xDE
    ];

    private readonly string _workspaces = TestScratch.NewPath("chat-turn");
    private readonly Mock<IEventPublisher> _publisher = new();
    private readonly List<CliRunFinishedEvent> _finished = [];

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<CliRun, Guid>, EFCoreRepository<CliChatDbContext, CliRun, Guid>>();
        services.AddScoped<IRepository<CliRunMessage, Guid>, EFCoreRepository<CliChatDbContext, CliRunMessage, Guid>>();
        services.AddScoped<IRepository<CliAgentBinding, Guid>, EFCoreRepository<CliChatDbContext, CliAgentBinding, Guid>>();
        services.AddScoped<IRepository<CliRuntime, Guid>, EFCoreRepository<CliChatDbContext, CliRuntime, Guid>>();
        services.AddScoped<IRepository<Agent, Guid>, EFCoreRepository<CliChatDbContext, Agent, Guid>>();
        services.AddScoped<IRepository<AgentThread, Guid>, EFCoreRepository<CliChatDbContext, AgentThread, Guid>>();
        services.AddScoped<IRepository<AgentThreadMessage, Guid>, EFCoreRepository<CliChatDbContext, AgentThreadMessage, Guid>>();
        services.AddScoped<IRepository<AgentAttachment, Guid>, EFCoreRepository<CliChatDbContext, AgentAttachment, Guid>>();
        services.AddScoped<AgentThreadService>();
        services.AddScoped<IAgentThreadService>(sp => sp.GetRequiredService<AgentThreadService>());
        services.AddScoped<IAgentThreadInternalService>(sp => sp.GetRequiredService<AgentThreadService>());
        services.AddScoped<IAgentAttachmentService, AgentAttachmentService>();
        services.AddScoped(_ => _publisher.Object);

        // 运行与线程的 DTO 投影要 Mapster：用默认配置，与其它调度器测试同一做法
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
    }

    public override void Dispose()
    {
        TestScratch.Delete(_workspaces);
        base.Dispose();
    }

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
        _finished.ShouldHaveSingleItem().AssistantMessageId.ShouldBe(reply.Id);

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

        var finished = _finished.ShouldHaveSingleItem();
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

    // ---------------------------------------------------------------- titles

    [Fact]
    public async Task TheFirstReplyOfAThread_TitlesIt_EvenWithNoClientConnected()
    {
        var (threadId, userMessageId) = await SeedThreadWithQuestionAsync("plan my week");
        var run = await SeedClaimedRunAsync(threadId, userMessageId, prompt: "plan my week");

        await CreateExecutor(new ScriptedAdapter([new CliAgentEvent { Type = CliAgentEventType.Text, Content = "Sure." }]))
            .ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        _publisher.Verify(p => p.HandleNewThreadTitleAsync(
            It.Is<AgentRunRequest>(r => r.ThreadId == threadId && r.UserMessage == "plan my week"),
            It.Is<AgentRunResult>(r => r.Response == "Sure."),
            "plan my week"), Times.Once);
    }

    [Fact]
    public async Task ALaterReply_DoesNotRetitleTheThread()
    {
        var (threadId, firstQuestion) = await SeedThreadWithQuestionAsync("first");
        await AddMessageAsync(threadId, MessageRole.Assistant, "first answer");
        var secondQuestion = await AddMessageAsync(threadId, MessageRole.User, "second");
        var run = await SeedClaimedRunAsync(threadId, secondQuestion);

        await CreateExecutor(new ScriptedAdapter([new CliAgentEvent { Type = CliAgentEventType.Text, Content = "Sure." }]))
            .ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        _publisher.Verify(p => p.HandleNewThreadTitleAsync(
            It.IsAny<AgentRunRequest>(), It.IsAny<AgentRunResult>(), It.IsAny<string?>()), Times.Never);
        firstQuestion.ShouldNotBe(Guid.Empty);
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
        context.Prompt.ShouldNotContain("could not be resumed");

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
        context.Prompt.ShouldContain("could not be resumed");
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
        context.Prompt.ShouldNotContain("could not be resumed");
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
        context.Prompt.ShouldContain("Current time: 10:00 UTC", Case.Sensitive, "the handler's context is appended to the prompt");
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

    // ---------------------------------------------------------------- helpers

    /// <summary>Streams a turn through the facade, playing the queue processor's part when the run starts.</summary>
    private async Task<List<AgentStreamChunk>> StreamThroughAsync(AgentDispatchFacade facade, ScriptedAdapter adapter, AgentRunRequest request)
    {
        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in facade.RunStreamingAsync(request))
        {
            chunks.Add(chunk);
            if (chunk.EventType == RunStreamEventTypes.RunStarted)
            {
                await ClaimAsync(chunk.RunId!.Value);
                await CreateExecutor(adapter).ExecuteAsync(chunk.RunId.Value, CancellationToken.None).WaitAsync(TestTimeout);
            }
        }

        return chunks;
    }

    private AgentDispatchFacade CreateFacade(ICliAgentTurnHandler handler)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => CreateDispatcher());
        services.AddScoped<ICliAgentDispatcher>(sp => sp.GetRequiredService<CliAgentDispatcher>());
        services.AddScoped(_ => ServiceProvider.GetRequiredService<IAgentThreadInternalService>());
        services.AddScoped(_ => handler);

        var binding = new Mock<ICliAgentBindingService>();
        binding.Setup(b => b.GetByAgentIdAsync(AgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CliAgentBindingDto { AgentId = AgentId, CliRuntimeId = RuntimeId, ProviderKey = "claude", AcceptsAttachments = true });

        return new AgentDispatchFacade(
            Mock.Of<IAgentRuntime>(MockBehavior.Strict),
            binding.Object,
            CreateDispatcher(),
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentDispatchFacade>.Instance);
    }

    private CliAgentDispatcher CreateDispatcher()
    {
        var registry = new Mock<ICliProviderRegistry>();
        registry.Setup(r => r.Find("claude")).Returns(CliBuiltInProviders.All["claude"]);

        return new CliAgentDispatcher(
            ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRunMessage, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliAgentBinding, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRuntime, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<AgentThread, Guid>>(),
            TestAgents.Repository(AgentId),
            new CliRunSignalHub(),
            new CliRunCancellationRegistry(),
            Options(new CliAgentOptions { Enabled = true, PollInterval = TimeSpan.FromMilliseconds(50) }),
            ServiceProvider,
            providerRegistry: registry.Object,
            attachmentService: ServiceProvider.GetRequiredService<IAgentAttachmentService>(),
            threadMessageRepository: ServiceProvider.GetRequiredService<IRepository<AgentThreadMessage, Guid>>());
    }

    private CliRunExecutor CreateExecutor(ScriptedAdapter adapter, CliToolResultOptions? toolResults = null, bool supportsResumeAt = true)
    {
        var descriptor = CliBuiltInProviders.All["claude"] with { SupportsResumeAtEntry = supportsResumeAt };
        var registry = new Mock<ICliProviderRegistry>();
        registry.Setup(r => r.Find("claude")).Returns(descriptor);

        var adapters = new Mock<ICliProtocolAdapterFactory>();
        adapters.Setup(f => f.IsImplemented(CliAgentProtocol.StreamJson)).Returns(true);
        adapters.Setup(f => f.Create(CliAgentProtocol.StreamJson)).Returns(adapter);

        var host = TestCliHost.Starting(() => new FakeProcess());

        var workspaceRoot = Path.Combine(_workspaces, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        var workspace = new CliWorkspace
        {
            RootDirectory = workspaceRoot, WorkDirectory = workspaceRoot, OutputDirectory = workspaceRoot, LogDirectory = workspaceRoot
        };
        var preparer = new Mock<ICliWorkspacePreparer>();
        preparer.Setup(p => p.PrepareAsync(It.IsAny<ICliHostTarget>(), It.IsAny<CliRunContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(workspace);
        preparer.Setup(p => p.ReuseAsync(It.IsAny<ICliHostTarget>(), It.IsAny<string>(), It.IsAny<CliRunContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CliWorkspace?)null);

        var eventBus = new Mock<IEventBus>();
        eventBus.Setup(b => b.PublishAsync(It.IsAny<CliRunFinishedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<CliRunFinishedEvent, CancellationToken>((e, _) => _finished.Add(e))
            .Returns(Task.CompletedTask);

        var runs = ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>();
        return new CliRunExecutor(
            runs,
            ServiceProvider.GetRequiredService<IRepository<CliRunMessage, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliAgentBinding, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRuntime, Guid>>(),
            TestAgents.Repository(AgentId),
            registry.Object,
            adapters.Object,
            host,
            preparer.Object,
            Mock.Of<ICliBriefComposer>(),
            Mock.Of<ICliMcpConfigComposer>(),
            new CliRunTokenService(runs, NullLogger<CliRunTokenService>.Instance),
            new CliLaunchEnvironmentComposer(Mock.Of<ICliAuthStatusProbe>(), NullLogger<CliLaunchEnvironmentComposer>.Instance),
            Mock.Of<IAgentGrantService>(),
            Mock.Of<ISkillService>(),
            new CliRunSignalHub(),
            Options(new CliAgentOptions { Enabled = true, ToolResults = toolResults ?? new CliToolResultOptions() }),
            ServiceProvider.GetRequiredService<ICurrentTenant>(),
            NullLogger<CliRunExecutor>.Instance,
            ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            threadRepository: ServiceProvider.GetRequiredService<IRepository<AgentThread, Guid>>(),
            eventBus: eventBus.Object);
    }

    private static IOptionsMonitor<CliAgentOptions> Options(CliAgentOptions value)
    {
        var monitor = new Mock<IOptionsMonitor<CliAgentOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(value);
        return monitor.Object;
    }

    private async Task SeedBindingAsync()
    {
        DbContext.Set<Agent>().Add(new Agent { Id = AgentId, Name = "cli-bound", Provider = "claude" });
        DbContext.Set<CliAgentBinding>().Add(new CliAgentBinding
        {
            AgentId = AgentId, CliRuntimeId = RuntimeId, WorkDirectoryMode = CliWorkDirectoryMode.PerThread,
            MaterializeSkills = false, InjectAgentInstructions = false
        });
        DbContext.Set<CliRuntime>().Add(new CliRuntime
        {
            Id = RuntimeId, HostId = "TEST-HOST", ProviderKey = "claude", Name = "claude @ TEST-HOST",
            ExecutablePath = "/nonexistent/claude", Status = CliRuntimeStatus.Online
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private async Task<Guid> SeedThreadAsync()
    {
        if (!await DbContext.Set<CliAgentBinding>().AnyAsync())
        {
            await SeedBindingAsync();
        }

        var thread = new AgentThread { AgentId = AgentId, Title = "Thread", LastActivityTime = DateTime.UtcNow };
        DbContext.Set<AgentThread>().Add(thread);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return thread.Id;
    }

    private async Task<(Guid ThreadId, Guid UserMessageId)> SeedThreadWithQuestionAsync(string question)
    {
        var threadId = await SeedThreadAsync();
        var messageId = await AddMessageAsync(threadId, MessageRole.User, question);
        return (threadId, messageId);
    }

    private async Task<Guid> AddMessageAsync(Guid threadId, string role, string content)
    {
        using var scope = ServiceProvider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAgentThreadInternalService>()
            .SaveMessageAsync(threadId, role, content);
    }

    private async Task<CliRun> SeedClaimedRunAsync(Guid threadId, Guid userMessageId, string prompt = "a question")
    {
        var run = new CliRun
        {
            AgentId = AgentId, CliRuntimeId = RuntimeId, ThreadId = threadId, UserMessageId = userMessageId,
            Status = CliRunStatus.Dispatched, ClaimedByHostId = "TEST-HOST", DispatchedAt = DateTime.UtcNow,
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(5), Prompt = prompt
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return run;
    }

    private async Task SeedFinishedRunAsync(Guid threadId, Guid userMessageId, string session, string entry, DateTime completedAt)
    {
        DbContext.Set<CliRun>().Add(new CliRun
        {
            AgentId = AgentId, CliRuntimeId = RuntimeId, ThreadId = threadId, UserMessageId = userMessageId,
            Status = CliRunStatus.Completed, Prompt = "earlier", ProviderSessionId = session, ProviderSessionEntryId = entry,
            CompletedAt = completedAt
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private async Task ClaimAsync(Guid runId)
        => await DbContext.Set<CliRun>().Where(r => r.Id == runId).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, CliRunStatus.Dispatched)
            .SetProperty(r => r.ClaimedByHostId, "TEST-HOST")
            .SetProperty(r => r.LeaseExpiresAt, DateTime.UtcNow.AddMinutes(5)));

    private async Task<CliRun> ReloadRunAsync(Guid runId)
        => await DbContext.Set<CliRun>().AsNoTracking().IgnoreQueryFilters().FirstAsync(r => r.Id == runId);

    private sealed class FixedContextHandler(string context) : ICliAgentTurnHandler
    {
        public Task<CliTurnDecision> OnTurnAsync(CliTurnContext turn, CancellationToken cancellationToken = default)
            => Task.FromResult(CliTurnDecision.Proceed(context));
    }

    private sealed class RecordingHandler : ICliAgentTurnHandler
    {
        public List<CliTurnContext> Turns { get; } = [];

        public List<CliTurnQueued> Queued { get; } = [];

        public Task<CliTurnDecision> OnTurnAsync(CliTurnContext turn, CancellationToken cancellationToken = default)
        {
            Turns.Add(turn);
            return Task.FromResult(CliTurnDecision.Proceed());
        }

        public Task OnTurnQueuedAsync(CliTurnQueued queued, CancellationToken cancellationToken = default)
        {
            Queued.Add(queued);
            return Task.CompletedTask;
        }
    }

    private sealed class RefusingHandler : ICliAgentTurnHandler
    {
        public Task<CliTurnDecision> OnTurnAsync(CliTurnContext turn, CancellationToken cancellationToken = default)
            => Task.FromResult(CliTurnDecision.Refuse("Not in this session.", "APP_RESTRICTED"));
    }

    /// <summary>Replays a fixed event script once and ends with the given status, recording what it was launched with.</summary>
    private sealed class ScriptedAdapter(
        IReadOnlyList<CliAgentEvent> events,
        CliRunStatus status = CliRunStatus.Completed,
        string sessionId = "session-after",
        string? entryId = "entry-after",
        CliRunFailureReason? failureReason = null,
        DateTimeOffset? usageLimitResetsAt = null) : ICliProtocolAdapter
    {
        public List<CliAgentLaunchContext> Contexts { get; } = [];

        public CliAgentProtocol Protocol => CliAgentProtocol.StreamJson;

        public CliProcessSpec BuildProcess(CliAgentLaunchContext context)
        {
            Contexts.Add(context);
            return new CliProcessSpec { ExecutablePath = context.ExecutablePath, WorkingDirectory = context.WorkingDirectory };
        }

        public async IAsyncEnumerable<CliAgentEvent> RunAsync(
            ICliAgentTransport transport, CliAgentLaunchContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var evt in events)
            {
                await Task.Yield();
                yield return evt;
            }
        }

        public CliAgentResult GetResult(CliSessionOutcome outcome) => new()
        {
            Status = status,
            FailureReason = status == CliRunStatus.Completed ? null
                : failureReason ?? (status == CliRunStatus.Cancelled ? CliRunFailureReason.Cancelled : CliRunFailureReason.ProviderError),
            UsageLimitResetsAt = usageLimitResetsAt,
            Output = status == CliRunStatus.Completed ? string.Concat(events.Where(e => e.Type == CliAgentEventType.Text).Select(e => e.Content)) : null,
            Error = status == CliRunStatus.Completed ? null : "stopped",
            SessionId = sessionId,
            SessionEntryId = entryId,
            DurationMs = 1
        };
    }

    private sealed class FakeProcess : ICliProcess
    {
        public ICliAgentTransport Transport { get; } = new FakeCliAgentTransport([]);

        public int ProcessId => 4242;

        public bool HasExited { get; private set; }

        public int? ExitCode => HasExited ? 0 : null;

        public Task TerminateAsync(CancellationToken cancellationToken)
        {
            HasExited = true;
            return Task.CompletedTask;
        }

        public Task<int> WaitForExitAsync(CancellationToken cancellationToken) => Task.FromResult(0);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
