namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// Which conversations need their owner: a thread is running while a run of an external agent is queued or working on
/// it (not while the run only waits on its background tasks after answering), and unread once a run wrote a reply into
/// it after its owner last saw it. The real executor, dispatcher and thread service run on SQLite.
/// </summary>
public class ThreadAttentionTests : CliChatTurnTestBase
{
    private static readonly Guid Owner = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
    private static readonly Guid SomeoneElse = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2");

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // The thread service asks the dispatcher which threads have a run going on, and the module's provider which await their owner
        services.AddScoped<ICliAgentDispatcher>(_ => CreateDispatcher());
        services.AddScoped<IRepository<CliRunPermissionRequest, Guid>, EFCoreRepository<CliChatDbContext, CliRunPermissionRequest, Guid>>();
        services.AddScoped<IThreadAwaitingOwnerProvider, CliRunAwaitingOwnerProvider>();
    }

    [Fact]
    public async Task ARunThatOnlyWaitsOnBackgroundTasks_IsNotRunning_AndItsAnswerIsUnread()
    {
        var (threadId, userMessageId) = await SeedOwnedThreadAsync(Owner);
        var run = await SeedClaimedRunAsync(threadId, userMessageId);
        var observed = new List<(bool Running, bool Awaiting, bool Unread)>();

        var adapter = new ScriptedAdapter(
        [
            new CliAgentEvent { Type = CliAgentEventType.Text, Content = "Starting the server." },
            new CliAgentEvent
            {
                Type = CliAgentEventType.TurnCompleted, Content = "Starting the server.",
                Turn = new CliTurnBoundary { BackgroundTasks = [new CliBackgroundTask("b1", "Dev server", "local_bash")] }
            },
            new CliAgentEvent { Type = CliAgentEventType.Status, Status = "background_task_finished", Content = "Dev server" },
            new CliAgentEvent { Type = CliAgentEventType.Text, Content = "The server stopped." }
        ], output: "The server stopped.", beforeEvent: async i =>
        {
            if (i > 0)
            {
                observed.Add((await IsRunningAsync(threadId), (await ReloadRunAsync(run.Id)).AwaitingFollowUp, (await ReadThreadAsync(threadId)).Unread));
            }
        });

        await CreateExecutor(adapter).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        observed.ShouldBe(
        [
            (true, false, false),  // after the first text: the turn is going on, nothing answered yet
            (false, true, true),   // after the turn boundary: answered, waiting on the dev server only
            (false, true, true)    // a status line does not start the next turn
        ]);

        var finished = await ReloadRunAsync(run.Id);
        finished.Status.ShouldBe(CliRunStatus.Completed);
        finished.AwaitingFollowUp.ShouldBeFalse("the CLI spoke again: the next turn started");
        (await IsRunningAsync(threadId)).ShouldBeFalse();
        (await ReadThreadAsync(threadId)).Unread.ShouldBeTrue();
    }

    [Fact]
    public async Task AQueuedRun_IsRunning_AndOnlyForItsThread()
    {
        var (threadId, userMessageId) = await SeedOwnedThreadAsync(Owner);
        var (otherThreadId, _) = await SeedOwnedThreadAsync(Owner);
        DbContext.Set<CliRun>().Add(new CliRun
        {
            AgentId = AgentId, CliRuntimeId = RuntimeId, ThreadId = threadId, UserMessageId = userMessageId,
            Status = CliRunStatus.Queued, Prompt = "queued"
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var running = await CreateDispatcher().GetRunningThreadIdsAsSystemAsync([threadId, otherThreadId]);
        running.ShouldNotBeNull().ShouldBe([threadId]);
        (await CreateDispatcher().GetRunningThreadIdsAsSystemAsync(null)).ShouldNotBeNull().ShouldBe([threadId]);
        (await CreateDispatcher().GetRunningThreadIdsAsSystemAsync([])).ShouldNotBeNull().ShouldBeEmpty();

        var thread = await ReadThreadAsync(threadId);
        thread.Running.ShouldBeTrue();
        thread.Unread.ShouldBeFalse();
        (await ReadThreadAsync(otherThreadId)).Running.ShouldBeFalse();
    }

    [Fact]
    public async Task AReplyWrittenAfterTheOwnerLastLooked_IsUnread_UntilTheyLookAgain()
    {
        var (threadId, userMessageId) = await SeedOwnedThreadAsync(Owner);
        await MarkReadAsync(threadId);

        var run = await SeedClaimedRunAsync(threadId, userMessageId);
        var adapter = new ScriptedAdapter([new CliAgentEvent { Type = CliAgentEventType.Text, Content = "Done." }]);
        await CreateExecutor(adapter).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var replied = await ReloadThreadAsync(threadId);
        replied.ReplyTime.ShouldNotBeNull();
        replied.ReplyTime.Value.ShouldBeGreaterThan(replied.ReadTime!.Value);
        (await ReadThreadAsync(threadId)).Unread.ShouldBeTrue("the reply came after the owner last looked");
        (await ListAsync()).Single(t => t.Id == threadId).Unread.ShouldBeTrue("the list says the same as the thread");

        await MarkReadAsync(threadId);

        (await ReadThreadAsync(threadId)).Unread.ShouldBeFalse();
        (await ListAsync()).Single(t => t.Id == threadId).Unread.ShouldBeFalse();
    }

    [Fact]
    public async Task ARunThatWritesNoReply_LeavesNothingUnread()
    {
        var (threadId, userMessageId) = await SeedOwnedThreadAsync(Owner);
        var run = await SeedClaimedRunAsync(threadId, userMessageId);
        var adapter = new ScriptedAdapter([new CliAgentEvent { Type = CliAgentEventType.Status, Status = "running" }],
            status: CliRunStatus.Failed, error: "crashed");

        await CreateExecutor(adapter).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        (await ReloadThreadAsync(threadId)).ReplyTime.ShouldBeNull();
        (await ReadThreadAsync(threadId)).Unread.ShouldBeFalse();
    }

    [Fact]
    public async Task ReplyAndReadTimes_OnlyMoveForward()
    {
        var (threadId, _) = await SeedOwnedThreadAsync(Owner);
        var later = DateTime.UtcNow.AddMinutes(10);

        using (var scope = ServiceProvider.CreateScope())
        {
            var threads = scope.ServiceProvider.GetRequiredService<IAgentThreadInternalService>();
            await threads.MarkRepliedAsync(threadId, later);
            await threads.MarkRepliedAsync(threadId, later.AddMinutes(-5));
        }

        (await ReloadThreadAsync(threadId)).ReplyTime!.Value.ShouldBe(later, TimeSpan.FromMilliseconds(1));

        // Read now: still earlier than the reply, so the thread stays unread; a later read clears it and a stale one does not undo it
        await MarkReadAsync(threadId);
        (await ReadThreadAsync(threadId)).Unread.ShouldBeTrue();

        await DbContext.Set<AgentThread>().Where(t => t.Id == threadId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ReadTime, (DateTime?)later.AddMinutes(1)));
        await MarkReadAsync(threadId);
        (await ReloadThreadAsync(threadId)).ReadTime!.Value.ShouldBe(later.AddMinutes(1), TimeSpan.FromMilliseconds(1));
        (await ReadThreadAsync(threadId)).Unread.ShouldBeFalse();
    }

    [Fact]
    public async Task MarkingAMissingThreadRead_Answers404()
    {
        using var scope = ServiceProvider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IAgentThreadService>().MarkReadAsync(Guid.NewGuid());

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
        result.ErrorCode.ShouldBe(ErrorCodes.ThreadNotFound);
    }

    [Fact]
    public async Task Attention_CountsTheOwnersRunningAndUnreadThreads_OfTheGivenAgents()
    {
        var (running, runningMessage) = await SeedOwnedThreadAsync(Owner);
        var (unread, _) = await SeedOwnedThreadAsync(Owner);
        var (read, _) = await SeedOwnedThreadAsync(Owner);
        var (othersUnread, _) = await SeedOwnedThreadAsync(SomeoneElse);
        var (othersRunning, othersMessage) = await SeedOwnedThreadAsync(SomeoneElse);

        DbContext.Set<CliRun>().AddRange(
            new CliRun { AgentId = AgentId, CliRuntimeId = RuntimeId, ThreadId = running, UserMessageId = runningMessage, Status = CliRunStatus.Running, Prompt = "a" },
            new CliRun { AgentId = AgentId, CliRuntimeId = RuntimeId, ThreadId = othersRunning, UserMessageId = othersMessage, Status = CliRunStatus.Running, Prompt = "b" });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        using (var scope = ServiceProvider.CreateScope())
        {
            var threads = scope.ServiceProvider.GetRequiredService<IAgentThreadInternalService>();
            foreach (var id in new[] { unread, read, othersUnread })
            {
                await threads.MarkRepliedAsync(id, DateTime.UtcNow.AddSeconds(-1));
            }
        }

        await MarkReadAsync(read);

        var attention = await AttentionAsync(new ThreadAttentionQueryDto { CreatorId = Owner });
        attention.RunningCount.ShouldBe(1);
        attention.RunningThreadIds.ShouldBe([running]);
        attention.UnreadCount.ShouldBe(1);
        attention.UnreadThreadIds.ShouldBe([unread]);

        var otherAgent = await AttentionAsync(new ThreadAttentionQueryDto { CreatorId = Owner, AgentIds = [Guid.NewGuid()] });
        otherAgent.RunningCount.ShouldBe(0);
        otherAgent.UnreadCount.ShouldBe(0);

        var thisAgent = await AttentionAsync(new ThreadAttentionQueryDto { CreatorId = Owner, AgentIds = [AgentId] });
        thisAgent.RunningCount.ShouldBe(1);
        thisAgent.UnreadCount.ShouldBe(1);
    }

    [Fact]
    public async Task APendingRequestOfALiveRun_PutsTheThreadFirst_UntilItIsAnsweredExpiresOrTheRunEnds()
    {
        var (quiet, _) = await SeedOwnedThreadAsync(Owner);
        var (asking, askingMessage) = await SeedOwnedThreadAsync(Owner);
        var (newest, _) = await SeedOwnedThreadAsync(Owner);
        await DbContext.Set<AgentThread>().Where(t => t.Id == asking)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.LastUserMessageTime, (DateTime?)DateTime.UtcNow.AddDays(-3)));

        var run = new CliRun
        {
            AgentId = AgentId, CliRuntimeId = RuntimeId, ThreadId = asking, UserMessageId = askingMessage,
            Status = CliRunStatus.Running, Prompt = "deploy"
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();
        var request = new CliRunPermissionRequest
        {
            RunId = run.Id, Sequence = 3, ProtocolKey = "p1", Tool = "Bash", ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        };
        DbContext.Set<CliRunPermissionRequest>().Add(request);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var list = await ListAsync();
        list.Select(t => t.Id).Take(2).ShouldBe([asking, newest], "awaiting the owner beats a newer message");
        list[0].AwaitingOwner.ShouldBeTrue();
        list.Single(t => t.Id == quiet).AwaitingOwner.ShouldBeFalse();
        (await ReadThreadAsync(asking)).AwaitingOwner.ShouldBeTrue();
        var attention = await AttentionAsync(new ThreadAttentionQueryDto { CreatorId = Owner });
        attention.AwaitingOwnerCount.ShouldBe(1);
        attention.AwaitingOwnerThreadIds.ShouldBe([asking]);
        (await AttentionAsync(new ThreadAttentionQueryDto { CreatorId = SomeoneElse })).AwaitingOwnerCount.ShouldBe(0);

        // Expired: the CLI is told no, nobody is waited for any more (the run itself still runs)
        await Request(request.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.ExpiresAt, DateTime.UtcNow.AddSeconds(-1)));
        (await ReadThreadAsync(asking)).AwaitingOwner.ShouldBeFalse();
        (await ListAsync())[0].Id.ShouldBe(asking, "still running, so still above the rest");

        // Answered
        await Request(request.Id).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.ExpiresAt, DateTime.UtcNow.AddMinutes(10))
            .SetProperty(r => r.Status, CliPermissionStatus.Allowed));
        (await ReadThreadAsync(asking)).AwaitingOwner.ShouldBeFalse();

        // Pending again, but the run is over: not waited for
        await Request(request.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, CliPermissionStatus.Pending));
        (await ReadThreadAsync(asking)).AwaitingOwner.ShouldBeTrue();
        await DbContext.Set<CliRun>().Where(r => r.Id == run.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, CliRunStatus.Completed));
        (await ReadThreadAsync(asking)).AwaitingOwner.ShouldBeFalse();
        (await ListAsync()).Select(t => t.Id).ShouldBe([newest, quiet, asking], "back in its place by the owner's last message");
    }

    // ---------------------------------------------------------------- helpers

    private IQueryable<CliRunPermissionRequest> Request(Guid requestId)
        => DbContext.Set<CliRunPermissionRequest>().Where(r => r.Id == requestId);

    private async Task<(Guid ThreadId, Guid UserMessageId)> SeedOwnedThreadAsync(Guid owner)
    {
        var seeded = await SeedThreadWithQuestionAsync("start the dev server");
        await DbContext.Set<AgentThread>().Where(t => t.Id == seeded.ThreadId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.CreatorId, (Guid?)owner));
        return seeded;
    }

    private async Task<bool> IsRunningAsync(Guid threadId)
        => (await CreateDispatcher().GetRunningThreadIdsAsSystemAsync([threadId]))!.Contains(threadId);

    private async Task<AgentThread> ReloadThreadAsync(Guid threadId)
        => await DbContext.Set<AgentThread>().AsNoTracking().IgnoreQueryFilters().FirstAsync(t => t.Id == threadId);

    private async Task<AgentThreadDetailDto> ReadThreadAsync(Guid threadId)
    {
        using var scope = ServiceProvider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IAgentThreadService>().GetDetailAsync(threadId);
        return result.Data.ShouldNotBeNull();
    }

    private async Task<IList<AgentThreadDto>> ListAsync()
    {
        using var scope = ServiceProvider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IAgentThreadService>()
            .GetListAsync(new ThreadListQueryDto { CreatorId = Owner, PageSize = 50 });
        return result.Data.ShouldNotBeNull().Items;
    }

    private async Task MarkReadAsync(Guid threadId)
    {
        using var scope = ServiceProvider.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<IAgentThreadService>().MarkReadAsync(threadId)).Succeeded.ShouldBeTrue();
    }

    private async Task<ThreadAttentionDto> AttentionAsync(ThreadAttentionQueryDto query)
    {
        using var scope = ServiceProvider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IAgentThreadService>().GetAttentionAsync(query);
        return result.Data.ShouldNotBeNull();
    }
}
