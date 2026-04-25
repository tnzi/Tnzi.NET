namespace Tnzi.AI.Tests;

public class RunTrackerTests
{
    [Fact]
    public async Task CreateRunAsync_PopulatesFieldsFromRequestAndResolution()
    {
        var (tracker, runStore, _, created, _) = Build();
        var agentId = Guid.NewGuid();
        var threadId = Guid.NewGuid();

        var request = new AgentRunRequest
        {
            AgentId = agentId,
            ThreadId = threadId,
            UserMessage = new string('x', 600)
        };
        var resolution = AgentResolution.SuccessWithoutExecutor(
            "openai", "gpt-4o", agentId, null, AgentExecutionMode.Single);

        var run = await tracker.CreateRunAsync(request, resolution, CancellationToken.None);

        run.AgentId.ShouldBe(agentId);
        run.ThreadId.ShouldBe(threadId);
        run.Status.ShouldBe(AgentRunStatus.Running);
        run.ExecutionMode.ShouldBe(AgentExecutionMode.Single);
        run.InputSummary!.Length.ShouldBe(503); // 500 chars + "..."
        run.LastHeartbeatAt.HasValue.ShouldBeTrue();
        created.Count.ShouldBe(1);
        runStore.Verify(x => x.CreateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetOrCreateRunAsync_WithoutExistingRunId_DelegatesToCreate()
    {
        var (tracker, runStore, _, _, _) = Build();
        var request = new AgentRunRequest { UserMessage = "hi" };
        var resolution = AgentResolution.SuccessWithoutExecutor("openai", "gpt-4o", Guid.NewGuid(), null, AgentExecutionMode.Single);

        await tracker.GetOrCreateRunAsync(request, resolution, CancellationToken.None);

        runStore.Verify(x => x.CreateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()), Times.Once);
        runStore.Verify(x => x.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetOrCreateRunAsync_WithExistingRunId_MergesRequestAndResolution()
    {
        var existingId = Guid.NewGuid();
        var existing = new AgentRun { Id = existingId, Status = AgentRunStatus.Failed };
        var (tracker, runStore, _, _, updated) = Build(setupExistingRun: existing);

        var request = new AgentRunRequest
        {
            ExistingRunId = existingId,
            AgentId = Guid.NewGuid(),
            ThreadId = Guid.NewGuid(),
            UserMessage = "resumed"
        };
        var resolution = AgentResolution.SuccessWithoutExecutor("openai", "gpt-4o", request.AgentId!.Value, null, AgentExecutionMode.Handoff);

        var run = await tracker.GetOrCreateRunAsync(request, resolution, CancellationToken.None);

        run.Id.ShouldBe(existingId);
        run.Status.ShouldBe(AgentRunStatus.Running); // reset to Running
        run.ExecutionMode.ShouldBe(AgentExecutionMode.Handoff);
        updated.Count.ShouldBe(1);
    }

    [Fact]
    public async Task GetOrCreateRunAsync_ExistingRunIdNotFound_ThrowsBusinessException()
    {
        var (tracker, _, _, _, _) = Build(setupExistingRun: null, configureGetToReturnNull: true);

        var request = new AgentRunRequest { ExistingRunId = Guid.NewGuid() };
        var resolution = AgentResolution.SuccessWithoutExecutor("openai", "gpt-4o", Guid.NewGuid(), null, AgentExecutionMode.Single);

        var ex = await Should.ThrowAsync<Tnzi.Exceptions.BusinessException>(
            () => tracker.GetOrCreateRunAsync(request, resolution, CancellationToken.None));

        ex.Code.ShouldBe(ErrorCodes.RunNotFound);
        ex.HttpStatusCode.ShouldBe(404);
    }

    [Fact]
    public async Task RecordTraceAsync_PersistsTraceAndSerializesEventData()
    {
        var (tracker, _, traceStore, _, _) = Build();
        var runId = Guid.NewGuid();

        await tracker.RecordTraceAsync(runId, null, AgentTraceEventTypes.RunCompleted,
            new { sample = 42 }, 1500, CancellationToken.None);

        traceStore.Verify(x => x.AddAsync(
            It.Is<AgentRunTrace>(t =>
                t.RunId == runId
                && t.EventType == AgentTraceEventTypes.RunCompleted
                && t.DurationMs == 1500
                && t.EventData != null
                && t.EventData.Contains("42")),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RecordTraceAsync_StoreThrows_SwallowsException()
    {
        var runStore = CreateRunStoreMock(new List<AgentRun>(), new List<AgentRun>(), null);
        var traceStore = new Mock<ITraceStore>();
        traceStore.Setup(x => x.AddAsync(It.IsAny<AgentRunTrace>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var tracker = new RunTracker(runStore.Object, traceStore.Object, Mock.Of<ILogger<RunTracker>>());

        // Should not throw
        await tracker.RecordTraceAsync(Guid.NewGuid(), null, "x", null, 0, CancellationToken.None);
    }

    [Fact]
    public async Task UpdateRunOnCompletionAsync_SetsUsageAndStatus()
    {
        var (tracker, _, traceStore, _, updated) = Build();
        var run = new AgentRun { Id = Guid.NewGuid(), Status = AgentRunStatus.Running };
        var result = new AgentRunResult
        {
            Response = "ok",
            Status = AgentRunStatus.Completed,
            Usage = new TokenUsageDto { InputTokens = 100, OutputTokens = 200 }
        };

        await tracker.UpdateRunOnCompletionAsync(run, result, 1200, CancellationToken.None);

        updated.Count.ShouldBe(1);
        updated[0].Status.ShouldBe(AgentRunStatus.Completed);
        updated[0].DurationMs.ShouldBe(1200);
        updated[0].TotalInputTokens.ShouldBe(100);
        updated[0].TotalOutputTokens.ShouldBe(200);
        updated[0].OutputSummary.ShouldBe("ok");
        updated[0].Error.ShouldBeNull();
        traceStore.Verify(x => x.AddAsync(
            It.Is<AgentRunTrace>(t => t.EventType == AgentTraceEventTypes.RunCompleted),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateRunOnCompletionAsync_FailedStatus_SetsErrorFromResponse()
    {
        var (tracker, _, _, _, updated) = Build();
        var run = new AgentRun { Id = Guid.NewGuid(), Status = AgentRunStatus.Running };
        var result = new AgentRunResult
        {
            Response = "blocked by policy",
            Status = AgentRunStatus.Failed
        };

        await tracker.UpdateRunOnCompletionAsync(run, result, 500, CancellationToken.None);

        updated[0].Status.ShouldBe(AgentRunStatus.Failed);
        updated[0].Error.ShouldBe("blocked by policy");
    }

    [Fact]
    public async Task UpdateRunOnFailureAsync_SetsErrorAndRecordsTrace()
    {
        var (tracker, _, traceStore, _, updated) = Build();
        var run = new AgentRun { Id = Guid.NewGuid(), Status = AgentRunStatus.Running };
        var ex = new InvalidOperationException("boom");

        await tracker.UpdateRunOnFailureAsync(run, ex, 800, CancellationToken.None);

        updated[0].Status.ShouldBe(AgentRunStatus.Failed);
        updated[0].Error.ShouldBe("boom");
        updated[0].DurationMs.ShouldBe(800);
        traceStore.Verify(x => x.AddAsync(
            It.Is<AgentRunTrace>(t => t.EventType == AgentTraceEventTypes.Error),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateRunOnFailureAsync_LongException_TruncatesTo1000Chars()
    {
        var (tracker, _, _, _, updated) = Build();
        var run = new AgentRun { Id = Guid.NewGuid() };
        var ex = new InvalidOperationException(new string('x', 1200));

        await tracker.UpdateRunOnFailureAsync(run, ex, 0, CancellationToken.None);

        updated[0].Error!.Length.ShouldBe(1003); // 1000 + "..."
    }

    [Fact]
    public async Task FinalizeStreamingCompletedAsync_SetsUsageAndTokens()
    {
        var (tracker, _, traceStore, _, updated) = Build();
        var run = new AgentRun { Id = Guid.NewGuid() };
        var streamResult = new AgentRunResult
        {
            Response = "streaming response",
            Status = AgentRunStatus.Completed,
            FinishReason = FinishReasons.Stop
        };

        await tracker.FinalizeStreamingCompletedAsync(run, streamResult, 50, 150, 2000, CancellationToken.None);

        updated[0].TotalInputTokens.ShouldBe(50);
        updated[0].TotalOutputTokens.ShouldBe(150);
        updated[0].DurationMs.ShouldBe(2000);
        updated[0].Status.ShouldBe(AgentRunStatus.Completed);
        updated[0].OutputSummary.ShouldBe("streaming response");
        traceStore.Verify(x => x.AddAsync(
            It.Is<AgentRunTrace>(t => t.EventType == AgentTraceEventTypes.StreamCompleted),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FinalizeStreamingCancelledAsync_SetsCancelledStatusAndTrace()
    {
        var (tracker, _, traceStore, _, updated) = Build();
        var run = new AgentRun { Id = Guid.NewGuid(), Status = AgentRunStatus.Running };

        await tracker.FinalizeStreamingCancelledAsync(run, FinishReasons.Stop, 1200, CancellationToken.None);

        updated[0].Status.ShouldBe(AgentRunStatus.Cancelled);
        updated[0].Error.ShouldBe("Streaming was cancelled by the caller");
        updated[0].DurationMs.ShouldBe(1200);
        traceStore.Verify(x => x.AddAsync(
            It.Is<AgentRunTrace>(t => t.EventType == AgentTraceEventTypes.StreamCancelled),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FinalizeStreamingFailedAsync_SetsFailedStatusAndTrace()
    {
        var (tracker, _, traceStore, _, updated) = Build();
        var run = new AgentRun { Id = Guid.NewGuid(), Status = AgentRunStatus.Running };

        await tracker.FinalizeStreamingFailedAsync(run, null, 300, CancellationToken.None);

        updated[0].Status.ShouldBe(AgentRunStatus.Failed);
        updated[0].Error.ShouldBe("Streaming execution failed");
        traceStore.Verify(x => x.AddAsync(
            It.Is<AgentRunTrace>(t => t.EventType == AgentTraceEventTypes.Error),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static (RunTracker tracker, Mock<IRunStore> runStore, Mock<ITraceStore> traceStore,
        List<AgentRun> created, List<AgentRun> updated) Build(
            AgentRun? setupExistingRun = null, bool configureGetToReturnNull = false)
    {
        var created = new List<AgentRun>();
        var updated = new List<AgentRun>();
        AgentRun? getResult = configureGetToReturnNull ? null : setupExistingRun;
        var runStore = CreateRunStoreMock(created, updated, getResult);
        var traceStore = new Mock<ITraceStore>();
        traceStore.Setup(x => x.AddAsync(It.IsAny<AgentRunTrace>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRunTrace trace, CancellationToken _) => trace);

        var tracker = new RunTracker(runStore.Object, traceStore.Object, Mock.Of<ILogger<RunTracker>>());
        return (tracker, runStore, traceStore, created, updated);
    }

    private static Mock<IRunStore> CreateRunStoreMock(List<AgentRun> created, List<AgentRun> updated, AgentRun? getResult)
    {
        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.CreateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()))
            .Callback<AgentRun, CancellationToken>((r, _) => created.Add(r))
            .ReturnsAsync((AgentRun r, CancellationToken _) =>
            {
                r.Id = Guid.NewGuid();
                return r;
            });
        runStore.Setup(x => x.UpdateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()))
            .Callback<AgentRun, CancellationToken>((r, _) => updated.Add(Clone(r)))
            .Returns(Task.CompletedTask);
        runStore.Setup(x => x.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(getResult);
        return runStore;
    }

    private static AgentRun Clone(AgentRun r) => new()
    {
        Id = r.Id,
        AgentId = r.AgentId,
        ThreadId = r.ThreadId,
        WorkflowDefinitionId = r.WorkflowDefinitionId,
        Status = r.Status,
        ExecutionMode = r.ExecutionMode,
        InputSummary = r.InputSummary,
        OutputSummary = r.OutputSummary,
        Error = r.Error,
        DurationMs = r.DurationMs,
        TotalInputTokens = r.TotalInputTokens,
        TotalOutputTokens = r.TotalOutputTokens,
        LastHeartbeatAt = r.LastHeartbeatAt
    };
}
