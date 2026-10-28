using Tnzi.Exceptions;

namespace Tnzi.AI.Tests;

/// <summary>
/// Chat rules applied before a turn runs, whichever engine runs it: who may chat with which agent, and editing or
/// regenerating a message (the thread is rewound first, then the turn runs as usual). Plus the run controls a chat
/// client needs for a turn that outlives its request: finding the run after a refresh, reattaching, cancelling.
/// </summary>
public class ChatServiceTurnRulesTests
{
    private static readonly Guid AgentId = Guid.NewGuid();
    private static readonly Guid ThreadId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    // ---------------------------------------------------------------- agent policy

    [Fact]
    public async Task APolicyRefusal_FailsTheTurnWithItsStatus_AndNothingRuns()
    {
        var facade = new RecordingFacade();
        var service = CreateService(facade, policy: new RefusingPolicy());

        var result = await service.ChatAsync(new ChatRequestDto { AgentId = AgentId, Message = "hi", UserId = UserId });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        result.ErrorCode.ShouldBe(ErrorCodes.ChatAgentNotAllowed);
        facade.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task APolicyRefusal_OnTheStream_IsAnHttpErrorBeforeAnyEvent()
    {
        var service = CreateService(new RecordingFacade(), policy: new RefusingPolicy());

        var error = await Should.ThrowAsync<BusinessException>(
            () => DrainAsync(service.ChatStreamingAsync(new ChatRequestDto { AgentId = AgentId, Message = "hi", UserId = UserId })));

        error.HttpStatusCode.ShouldBe(403);
        error.Code.ShouldBe(ErrorCodes.ChatAgentNotAllowed);
    }

    // ---------------------------------------------------------------- usage limits and model selection

    private static Mock<IUsageLimitService> LimitReached()
    {
        var limits = new Mock<IUsageLimitService>();
        limits.Setup(l => l.CheckAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UsageLimitDenialDto { Window = UsageLimitWindow.Session, ResetsAt = new DateTime(2026, 10, 21, 15, 30, 0, DateTimeKind.Utc) });
        return limits;
    }

    [Fact]
    public async Task UsageLimitReached_FailsTheTurnWith429_CarryingTheWindowAndResetTime_AndNothingRuns()
    {
        var facade = new RecordingFacade();
        var service = CreateService(facade, usageLimits: LimitReached().Object);

        var result = await service.ChatAsync(new ChatRequestDto { AgentId = AgentId, Message = "hi", UserId = UserId });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(429);
        result.ErrorCode.ShouldBe(ErrorCodes.UsageLimitReached);
        var denial = result.ErrorDetails.ShouldBeOfType<UsageLimitDenialDto>();
        denial.Window.ShouldBe(UsageLimitWindow.Session);
        denial.ResetsAt.ShouldBe(new DateTime(2026, 10, 21, 15, 30, 0, DateTimeKind.Utc));
        facade.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task UsageLimitReached_OnTheStream_IsAnHttpErrorBeforeAnyEvent_WithTheDetails()
    {
        var service = CreateService(new RecordingFacade(), usageLimits: LimitReached().Object);

        var error = await Should.ThrowAsync<BusinessException>(
            () => DrainAsync(service.ChatStreamingAsync(new ChatRequestDto { AgentId = AgentId, Message = "hi", UserId = UserId })));

        error.HttpStatusCode.ShouldBe(429);
        error.Code.ShouldBe(ErrorCodes.UsageLimitReached);
        error.ErrorDetails.ShouldBeOfType<UsageLimitDenialDto>().Window.ShouldBe(UsageLimitWindow.Session);
    }

    [Fact]
    public async Task Edit_WhenTheUsageLimitIsReached_DeletesNothing()
    {
        // 被拦下的一轮不能先删掉用户要替换的那几轮：上限查在回退之前
        var edited = new ThreadMessageRef(Guid.NewGuid(), 3, "old text");
        var threads = new FakeThreads(edited);
        var service = CreateService(new RecordingFacade(), threads: threads, usageLimits: LimitReached().Object);

        var result = await service.ChatAsync(new ChatRequestDto
        {
            AgentId = AgentId, ThreadId = ThreadId, UserId = UserId, EditMessageId = edited.Id, Message = "new text"
        });

        result.Code.ShouldBe(429);
        threads.DeletedFrom.ShouldBeEmpty();
    }

    [Fact]
    public async Task AModelTheUserMayNotUse_IsRefusedWith403_AndNothingRuns()
    {
        var policy = new Mock<IUsagePolicyService>();
        policy.Setup(p => p.ResolveChatModelAsync(UserId, AgentId, null, "claude-opus-5-5", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string?>.Failure("The model 'claude-opus-5-5' is not available to you for this assistant.", 403, ErrorCodes.ModelNotAllowed));
        var facade = new RecordingFacade();
        var service = CreateService(facade, usagePolicy: policy.Object);

        var result = await service.ChatAsync(new ChatRequestDto { AgentId = AgentId, Message = "hi", UserId = UserId, Model = "claude-opus-5-5" });

        result.Code.ShouldBe(403);
        result.ErrorCode.ShouldBe(ErrorCodes.ModelNotAllowed);
        facade.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheResolvedModel_IsTheOneThatRuns_IncludingTheUsersDefaultWhenNoneWasPicked()
    {
        var policy = new Mock<IUsagePolicyService>();
        policy.Setup(p => p.ResolveChatModelAsync(UserId, AgentId, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string?>.Success("claude-sonnet-5-5"));
        var facade = new RecordingFacade();
        var service = CreateService(facade, usagePolicy: policy.Object);

        await service.ChatAsync(new ChatRequestDto { AgentId = AgentId, Message = "hi", UserId = UserId });

        facade.Requests.ShouldHaveSingleItem().Model.ShouldBe("claude-sonnet-5-5");
    }

    // ---------------------------------------------------------------- edit

    [Fact]
    public async Task Edit_RemovesTheMessageAndEverythingAfterIt_ThenSendsTheNewText()
    {
        var edited = new ThreadMessageRef(Guid.NewGuid(), 3, "old text");
        var threads = new FakeThreads(edited);
        var facade = new RecordingFacade();
        var service = CreateService(facade, threads: threads);

        var result = await service.ChatAsync(new ChatRequestDto
        {
            AgentId = AgentId, ThreadId = ThreadId, UserId = UserId, EditMessageId = edited.Id, Message = "new text"
        });

        result.Succeeded.ShouldBeTrue();
        threads.DeletedFrom.ShouldBe([3]);
        var sent = facade.Requests.ShouldHaveSingleItem();
        sent.UserMessage.ShouldBe("new text");
        sent.ThreadId.ShouldBe(ThreadId);
        sent.RewoundTurns.ShouldBeTrue();
    }

    [Fact]
    public async Task Edit_OfAMessageThatIsNotTheUsersInThisThread_IsRefused_AndNothingIsDeleted()
    {
        var threads = new FakeThreads(target: null);
        var service = CreateService(new RecordingFacade(), threads: threads);

        var result = await service.ChatAsync(new ChatRequestDto
        {
            AgentId = AgentId, ThreadId = ThreadId, UserId = UserId, EditMessageId = Guid.NewGuid(), Message = "x"
        });

        result.Code.ShouldBe(400);
        result.ErrorCode.ShouldBe(ErrorCodes.MessageNotEditable);
        threads.DeletedFrom.ShouldBeEmpty();
    }

    [Fact]
    public async Task Edit_InSomeoneElsesThread_IsNotFound()
    {
        var threads = new FakeThreads(new ThreadMessageRef(Guid.NewGuid(), 1, "x")) { Owner = Guid.NewGuid() };
        var service = CreateService(new RecordingFacade(), threads: threads);

        var result = await service.ChatAsync(new ChatRequestDto
        {
            AgentId = AgentId, ThreadId = ThreadId, UserId = UserId, EditMessageId = Guid.NewGuid(), Message = "x"
        });

        result.Code.ShouldBe(404);
        threads.DeletedFrom.ShouldBeEmpty();
    }

    [Fact]
    public async Task Edit_WhileAReplyIsInProgress_IsAConflict_AndNothingIsDeleted()
    {
        var threads = new FakeThreads(new ThreadMessageRef(Guid.NewGuid(), 1, "x"));
        var facade = new RecordingFacade { ActiveRuns = [new ChatRunDto { RunId = Guid.NewGuid(), ThreadId = ThreadId }] };
        var service = CreateService(facade, threads: threads);

        var result = await service.ChatAsync(new ChatRequestDto
        {
            AgentId = AgentId, ThreadId = ThreadId, UserId = UserId, EditMessageId = Guid.NewGuid(), Message = "x"
        });

        result.Code.ShouldBe(409);
        result.ErrorCode.ShouldBe(ErrorCodes.ThreadRunInProgress);
        threads.DeletedFrom.ShouldBeEmpty("deleting a question a run is answering leaves its reply under nothing");
    }

    // ---------------------------------------------------------------- regenerate

    [Fact]
    public async Task Regenerate_SendsTheLastQuestionAgain_WithItsAttachments()
    {
        var last = new ThreadMessageRef(Guid.NewGuid(), 5, "describe this");
        var threads = new FakeThreads(last);
        var attachments = new Mock<IAgentAttachmentService>();
        attachments.Setup(a => a.GetForMessageAsync(last.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new AgentAttachment { Origin = AttachmentOrigin.Upload, MediaType = "image/png", FileName = "a.png", Content = [1, 2, 3] }]);
        var facade = new RecordingFacade();
        var service = CreateService(facade, threads: threads, attachments: attachments.Object);

        await service.ChatAsync(new ChatRequestDto { AgentId = AgentId, ThreadId = ThreadId, UserId = UserId, Regenerate = true });

        threads.FindCalls.ShouldBe([(Guid?)null], "regenerate targets the last user message");
        threads.DeletedFrom.ShouldBe([5], "the question is sent again as a new message, so the old one goes too");
        var sent = facade.Requests.ShouldHaveSingleItem();
        sent.UserMessage.ShouldBe("describe this");
        sent.ContentParts!.OfType<TextContentPartDto>().Single().Text.ShouldBe("describe this");
        var image = sent.ContentParts!.OfType<ImageContentPartDto>().Single();
        image.Base64Data.ShouldBe(Convert.ToBase64String([1, 2, 3]));
        image.FileName.ShouldBe("a.png");
        sent.RewoundTurns.ShouldBeTrue();
    }

    [Fact]
    public async Task Regenerate_WithNewContent_IsABadRequest()
    {
        var service = CreateService(new RecordingFacade(), threads: new FakeThreads(new ThreadMessageRef(Guid.NewGuid(), 1, "x")));

        var result = await service.ChatAsync(new ChatRequestDto
        {
            AgentId = AgentId, ThreadId = ThreadId, UserId = UserId, Regenerate = true, Message = "different"
        });

        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task EditOrRegenerate_WithoutAThread_IsABadRequest()
    {
        var service = CreateService(new RecordingFacade(), threads: new FakeThreads(null));

        var result = await service.ChatAsync(new ChatRequestDto { AgentId = AgentId, UserId = UserId, Regenerate = true });

        result.Code.ShouldBe(400);
    }

    // ---------------------------------------------------------------- per-turn metadata

    [Fact]
    public async Task Metadata_GoesToTheRun()
    {
        var facade = new RecordingFacade();
        var service = CreateService(facade);
        using var targets = JsonDocument.Parse("""["pc-1","pc-2"]""");

        var result = await service.ChatAsync(new ChatRequestDto
        {
            AgentId = AgentId, UserId = UserId, Message = "restart them",
            Metadata = new Dictionary<string, JsonElement> { ["targets"] = targets.RootElement }
        });

        result.Succeeded.ShouldBeTrue();
        var sent = facade.Requests.ShouldHaveSingleItem().TurnMetadata.ShouldNotBeNull();
        sent["targets"].EnumerateArray().Select(e => e.GetString()).ShouldBe(["pc-1", "pc-2"]);
    }

    [Fact]
    public async Task InvalidMetadata_IsABadRequest_BeforeAnythingIsRewound()
    {
        var threads = new FakeThreads(new ThreadMessageRef(Guid.NewGuid(), 3, "old"));
        var facade = new RecordingFacade();
        var service = CreateService(facade, threads: threads);
        using var value = JsonDocument.Parse("1");

        var result = await service.ChatAsync(new ChatRequestDto
        {
            AgentId = AgentId, ThreadId = ThreadId, UserId = UserId, EditMessageId = Guid.NewGuid(), Message = "new",
            Metadata = Enumerable.Range(0, 17).ToDictionary(i => $"k{i}", _ => value.RootElement)
        });

        result.Code.ShouldBe(400);
        result.ErrorCode.ShouldBe(ErrorCodes.InvalidTurnMetadata);
        threads.DeletedFrom.ShouldBeEmpty("a request that will be refused must not cost the user the turns it meant to replace");
        facade.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Regenerate_WithoutMetadata_SendsTheStoredMetadataAgain()
    {
        var last = new ThreadMessageRef(Guid.NewGuid(), 5, "restart them") { MetadataJson = """{"targets":["pc-1"]}""" };
        var facade = new RecordingFacade();
        var service = CreateService(facade, threads: new FakeThreads(last));

        await service.ChatAsync(new ChatRequestDto { AgentId = AgentId, ThreadId = ThreadId, UserId = UserId, Regenerate = true });

        var sent = facade.Requests.ShouldHaveSingleItem().TurnMetadata.ShouldNotBeNull();
        sent["targets"][0].GetString().ShouldBe("pc-1");
    }

    [Fact]
    public async Task Regenerate_WithMetadata_SendsTheGivenMetadata()
    {
        var last = new ThreadMessageRef(Guid.NewGuid(), 5, "restart them") { MetadataJson = """{"targets":["pc-1"]}""" };
        var facade = new RecordingFacade();
        var service = CreateService(facade, threads: new FakeThreads(last));
        using var targets = JsonDocument.Parse("""["pc-9"]""");

        await service.ChatAsync(new ChatRequestDto
        {
            AgentId = AgentId, ThreadId = ThreadId, UserId = UserId, Regenerate = true,
            Metadata = new Dictionary<string, JsonElement> { ["targets"] = targets.RootElement }
        });

        facade.Requests.Single().TurnMetadata!["targets"][0].GetString().ShouldBe("pc-9");
    }

    // ---------------------------------------------------------------- usage limit

    [Fact]
    public async Task UsageLimit_IsA429_WithTheResetTimeInTheErrorDetails()
    {
        var resetsAt = new DateTime(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);
        var facade = new RecordingFacade
        {
            RunResult = new AgentRunResult
            {
                Response = "You've hit your limit", FinishReason = FinishReasons.Error, Status = AgentRunStatus.Failed,
                ErrorCode = ErrorCodes.CliUsageLimit, UsageLimitResetsAt = resetsAt
            }
        };

        var result = await CreateService(facade).ChatAsync(new ChatRequestDto { AgentId = AgentId, UserId = UserId, Message = "hi" });

        result.Code.ShouldBe(429);
        result.ErrorCode.ShouldBe(ErrorCodes.CliUsageLimit);
        result.ErrorDetails.ShouldBeOfType<Dictionary<string, object>>()["usageLimitResetsAt"].ShouldBe(resetsAt);
    }

    [Fact]
    public async Task UsageLimit_OnTheStream_TheErrorAndDoneEventsCarryTheCodeAndTheResetTime()
    {
        var resetsAt = new DateTime(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);
        var facade = new RecordingFacade
        {
            StreamChunks =
            [
                new AgentStreamChunk
                {
                    FinishReason = FinishReasons.Error, Error = "You've hit your limit",
                    ErrorCode = ErrorCodes.CliUsageLimit, UsageLimitResetsAt = resetsAt
                }
            ]
        };

        var events = await DrainAsync(CreateService(facade).ChatStreamingAsync(new ChatRequestDto { AgentId = AgentId, UserId = UserId, Message = "hi" }));

        var error = events.First(e => e.IsError && !e.IsDone);
        error.ErrorCode.ShouldBe(ErrorCodes.CliUsageLimit);
        error.UsageLimitResetsAt.ShouldBe(resetsAt);
        var done = events.Single(e => e.IsDone);
        done.ErrorCode.ShouldBe(ErrorCodes.CliUsageLimit);
        done.UsageLimitResetsAt.ShouldBe(resetsAt);
    }

    // ---------------------------------------------------------------- run controls

    [Fact]
    public async Task ActiveRuns_AreOnlyListedForTheThreadsOwner()
    {
        var facade = new RecordingFacade { ActiveRuns = [new ChatRunDto { RunId = Guid.NewGuid(), ThreadId = ThreadId }] };
        var service = CreateService(facade, threads: new FakeThreads(null));

        (await service.GetActiveRunsAsync(ThreadId, UserId)).Data.ShouldHaveSingleItem();
        (await service.GetActiveRunsAsync(ThreadId, Guid.NewGuid())).Code.ShouldBe(404);
    }

    [Fact]
    public async Task Attach_StreamsTheRunAsChatEvents_WithItsRunIdAndThread()
    {
        var runId = Guid.NewGuid();
        var facade = new RecordingFacade
        {
            Run = new ChatRunDto { RunId = runId, ThreadId = ThreadId },
            AttachChunks =
            [
                new AgentStreamChunk { EventType = RunStreamEventTypes.RunStarted, RunId = runId },
                new AgentStreamChunk { Text = "more", Sequence = 7 },
                new AgentStreamChunk { FinishReason = FinishReasons.Stop, RunId = runId, AssistantMessageId = Guid.NewGuid() }
            ]
        };
        var service = CreateService(facade);

        var events = await DrainAsync(service.AttachStreamingAsync(runId, 6));

        facade.AttachedFrom.ShouldBe(6);
        events[0].EventType.ShouldBe(RunStreamEventTypes.RunStarted);
        events[0].RunId.ShouldBe(runId);
        events[0].ThreadId.ShouldBe(ThreadId);
        events[1].Delta.ShouldBe("more");
        events[1].Sequence.ShouldBe(7);
        var done = events.Single(e => e.IsDone);
        done.RunId.ShouldBe(runId);
        done.AssistantMessageId.ShouldNotBeNull();
    }

    [Fact]
    public async Task Attach_ToARunTheCallerCannotSee_IsNotFound()
    {
        var service = CreateService(new RecordingFacade());

        var error = await Should.ThrowAsync<BusinessException>(() => DrainAsync(service.AttachStreamingAsync(Guid.NewGuid())));

        error.HttpStatusCode.ShouldBe(404);
    }

    [Fact]
    public async Task Cancel_GoesToTheRunOnTheServer()
    {
        var facade = new RecordingFacade();
        var runId = Guid.NewGuid();

        (await CreateService(facade).CancelRunAsync(runId)).Succeeded.ShouldBeTrue();

        facade.Cancelled.ShouldBe([runId]);
    }

    // ---------------------------------------------------------------- helpers

    private static ChatService CreateService(
        RecordingFacade facade, IChatAgentPolicy? policy = null, FakeThreads? threads = null, IAgentAttachmentService? attachments = null,
        IUsageLimitService? usageLimits = null, IUsagePolicyService? usagePolicy = null)
        => new(
            facade,
            new StaticOptionsMonitor<AIOptions>(new AIOptions { DefaultProvider = "test" }),
            new ServiceCollection().AddLogging().BuildServiceProvider(),
            policy,
            threads,
            threads,
            attachments,
            usageLimits: usageLimits,
            usagePolicy: usagePolicy);

    private static async Task<List<StreamEvent>> DrainAsync(IAsyncEnumerable<StreamEvent> stream)
    {
        var events = new List<StreamEvent>();
        await foreach (var evt in stream)
        {
            events.Add(evt);
        }

        return events;
    }

    private sealed class RefusingPolicy : IChatAgentPolicy
    {
        public Task<Result> CheckAsync(Guid? agentId, Guid? userId, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Failure("This agent is not available for chat.", 403, ErrorCodes.ChatAgentNotAllowed));
    }

    private sealed class RecordingFacade : IAgentDispatchFacade
    {
        public List<AgentRunRequest> Requests { get; } = [];

        public List<Guid> Cancelled { get; } = [];

        public IReadOnlyList<ChatRunDto> ActiveRuns { get; init; } = [];

        public ChatRunDto? Run { get; init; }

        public IReadOnlyList<AgentStreamChunk> AttachChunks { get; init; } = [];

        public int? AttachedFrom { get; private set; }

        public AgentRunResult? RunResult { get; init; }

        public IReadOnlyList<AgentStreamChunk>? StreamChunks { get; init; }

        public Task<AgentRunResult> RunAsync(AgentRunRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(RunResult ?? new AgentRunResult { Response = "ok", FinishReason = FinishReasons.Stop, Status = AgentRunStatus.Completed });
        }

        public async IAsyncEnumerable<AgentStreamChunk> RunStreamingAsync(
            AgentRunRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            foreach (var chunk in StreamChunks ?? [new AgentStreamChunk { FinishReason = FinishReasons.Stop }])
            {
                await Task.Yield();
                yield return chunk;
            }
        }

        public Task<ChatRunDto?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
            => Task.FromResult(Run?.RunId == runId ? Run : null);

        public Task<IReadOnlyList<ChatRunDto>> GetActiveRunsAsync(Guid threadId, CancellationToken cancellationToken = default)
            => Task.FromResult(ActiveRuns);

        public async IAsyncEnumerable<AgentStreamChunk> AttachStreamingAsync(
            Guid runId, int fromSequence = 0, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            AttachedFrom = fromSequence;
            foreach (var chunk in AttachChunks)
            {
                await Task.Yield();
                yield return chunk;
            }
        }

        public Task<Result> CancelRunAsync(Guid runId, CancellationToken cancellationToken = default)
        {
            Cancelled.Add(runId);
            return Task.FromResult(Result.Success());
        }
    }

    /// <summary>Thread services for one thread owned by <see cref="UserId"/>, with one target message (or none).</summary>
    private sealed class FakeThreads(ThreadMessageRef? target) : IAgentThreadService, IAgentThreadInternalService
    {
        public Guid Owner { get; init; } = UserId;

        public List<int> DeletedFrom { get; } = [];

        public List<Guid?> FindCalls { get; } = [];

        public Task<bool> IsOwnerAsync(Guid threadId, Guid userId) => Task.FromResult(threadId == ThreadId && userId == Owner);

        public Task<ThreadMessageRef?> FindUserMessageAsync(Guid threadId, Guid? messageId, CancellationToken ct = default)
        {
            FindCalls.Add(messageId);
            return Task.FromResult(target);
        }

        public Task<int> DeleteMessagesFromAsync(Guid threadId, int fromOrder, CancellationToken ct = default)
        {
            DeletedFrom.Add(fromOrder);
            return Task.FromResult(2);
        }

        public Task<Result<AgentThreadDto>> CreateAsync(CreateAgentThreadDto input) => throw new NotSupportedException();
        public Task<Result<AgentThreadDto>> GetByIdAsync(Guid id) => throw new NotSupportedException();
        public Task<Result<AgentThreadDetailDto>> GetDetailAsync(Guid id, int messageLimit = 50) => throw new NotSupportedException();
        public Task<Result<IPagedList<AgentThreadDto>>> GetListAsync(ThreadListQueryDto query) => throw new NotSupportedException();
        public Task<Result<AgentThreadDto>> UpdateTitleAsync(Guid id, string title) => throw new NotSupportedException();
        public Task<Result> DeleteAsync(Guid id) => throw new NotSupportedException();
        public Task<Result<ThreadExportDto>> ExportAsJsonAsync(Guid id) => throw new NotSupportedException();
        public Task<Result<string>> ExportAsMarkdownAsync(Guid id) => throw new NotSupportedException();

        public Task<(Guid threadId, bool isNewThread)> GetOrCreateThreadAsync(Guid? threadId, Guid? agentId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Guid> SaveMessageAsync(Guid threadId, string role, string content, string? toolCalls = null, string? usage = null,
            Guid? messageId = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<List<ChatMessage>> GetMessageHistoryAsync(Guid threadId, int? limit = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<string?> GetMetadataValueAsync(Guid threadId, string key, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task SetMetadataValueAsync(Guid threadId, string key, string? valueJson, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
