namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 线程删除时，它的外部运行内容（运行记录、事件流、本机工作区）不再留下，还没结束的运行停下来。
/// </summary>
/// <remarks>
/// ★ 此前线程删除只清内建 agent 的表：外部运行的提示词、交付物与每一次工具调用的输入输出在线程删除之后照样留在库里，
/// 按运行 Id 仍然读得到，每线程工作区也留在磁盘上，要等回收器按闲置时长删。
/// </remarks>
public class CliThreadCleanupHandlerTests : IntegratedTestBase<CliThreadOwnershipDbContext>
{
    private static readonly Guid AgentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid RuntimeId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly string _workspacesRoot = TestScratch.NewPath("thread-cleanup");
    private readonly CliRunCancellationRegistry _registry = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<CliRun, Guid>, EFCoreRepository<CliThreadOwnershipDbContext, CliRun, Guid>>();
        services.AddScoped<IRepository<CliRunMessage, Guid>, EFCoreRepository<CliThreadOwnershipDbContext, CliRunMessage, Guid>>();
        services.AddScoped<IRepository<CliRuntime, Guid>, EFCoreRepository<CliThreadOwnershipDbContext, CliRuntime, Guid>>();
    }

    public override void Dispose()
    {
        TestScratch.Delete(_workspacesRoot);

        base.Dispose();
    }

    [Fact]
    public async Task FinishedRuns_AndTheirEvents_AreDeleted()
    {
        var thread = Guid.NewGuid();
        var completed = await SeedRunAsync(thread, CliRunStatus.Completed, "secret prompt", output: "secret answer");
        var failed = await SeedRunAsync(thread, CliRunStatus.Failed, "another prompt");
        await SeedMessagesAsync(completed, "tool output with file contents", "assistant text");
        await SeedMessagesAsync(failed, "stderr tail");

        await HandleAsync(thread);

        (await RunsAsync()).ShouldBeEmpty();
        (await MessagesAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task OtherThreads_AreUntouched()
    {
        var deleted = Guid.NewGuid();
        var kept = Guid.NewGuid();
        await SeedRunAsync(deleted, CliRunStatus.Completed, "gone");
        var keptRun = await SeedRunAsync(kept, CliRunStatus.Completed, "kept");
        var threadless = await SeedRunAsync(threadId: null, CliRunStatus.Completed, "no thread");
        await SeedMessagesAsync(keptRun, "kept event");
        await SeedMessagesAsync(threadless, "threadless event");

        await HandleAsync(deleted);

        (await RunsAsync()).Select(r => r.Id).ShouldBe([keptRun, threadless], ignoreOrder: true);
        (await MessagesAsync()).Select(m => m.Content).ShouldBe(["kept event", "threadless event"], ignoreOrder: true);
    }

    [Fact]
    public async Task QueuedRun_IsDeleted_SoItNeverExecutes()
    {
        var thread = Guid.NewGuid();
        await SeedRunAsync(thread, CliRunStatus.Queued, "not started yet");

        await HandleAsync(thread);

        (await RunsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task ExecutingRun_IsAskedToStop_ItsEventsAndPromptGo_ButItsRecordIsLeftForTheExecutor()
    {
        // 执行中的运行记录删不得：续租看到「行没了」只会默默退出、不去杀进程，终态回写也会失败。
        // 记录留给执行器，它收尾时发现线程已删除就整条删掉（CliRunDeletedThreadTests）。
        var thread = Guid.NewGuid();
        var running = await SeedRunAsync(thread, CliRunStatus.Running, "the prompt", perTurnContext: "per-turn context");
        await SeedMessagesAsync(running, "streamed so far");
        using var local = _registry.Register(running, CancellationToken.None);

        await HandleAsync(thread);

        var row = (await RunsAsync()).ShouldHaveSingleItem();
        row.Id.ShouldBe(running);
        row.CancelRequested.ShouldBeTrue();
        row.Prompt.ShouldBeEmpty();
        row.PerTurnContext.ShouldBeNull();
        local.IsCancellationRequested.ShouldBeTrue("a run executing on this replica is aborted at once");
        (await MessagesAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task SoftDeletedRunRows_AreRemovedToo()
    {
        // 软删的行里内容同样都在
        var thread = Guid.NewGuid();
        var run = await SeedRunAsync(thread, CliRunStatus.Completed, "soft deleted prompt");
        await DbContext.Set<CliRun>().IgnoreQueryFilters().Where(r => r.Id == run)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsDeleted, true));

        await HandleAsync(thread);

        (await RunsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task PerThreadWorkspace_IsRemoved_WhenNothingOfTheThreadIsStillExecuting()
    {
        var thread = Guid.NewGuid();
        await SeedRunAsync(thread, CliRunStatus.Completed, "x");
        var threadRoot = CreateRunRoot("host", thread);
        var partitionedRoot = CreateRunRoot(Path.Combine("host", CliWorkspaceLayout.UserPartition(Guid.NewGuid())), thread);
        var otherThreadRoot = CreateRunRoot("host", Guid.NewGuid());

        await HandleAsync(thread);

        Directory.Exists(threadRoot).ShouldBeFalse();
        Directory.Exists(partitionedRoot).ShouldBeFalse("both directory shapes are searched");
        Directory.Exists(otherThreadRoot).ShouldBeTrue();
    }

    [Fact]
    public async Task PerWorkspaceConfigDirectory_TheCliTranscriptGoesWithTheThread()
    {
        // 每工作区配置目录下，CLI 自己的会话记录（含工具输出）在运行根里，删除线程时随工作区一起删掉
        var thread = Guid.NewGuid();
        await SeedRunAsync(thread, CliRunStatus.Completed, "x");
        var threadRoot = CreateRunRoot("host", thread);
        var transcript = WorkspaceGcTests.WriteTranscript(threadRoot);

        await HandleAsync(thread);

        File.Exists(transcript).ShouldBeFalse();
    }

    [Fact]
    public async Task PerThreadWorkspace_IsKept_WhileARunOfTheThreadIsStillExecuting()
    {
        var thread = Guid.NewGuid();
        await SeedRunAsync(thread, CliRunStatus.Running, "x");
        var threadRoot = CreateRunRoot("host", thread);

        await HandleAsync(thread);

        Directory.Exists(threadRoot).ShouldBeTrue("the executing run is still using it; the executor removes it when the run ends");
    }

    [Fact]
    public async Task PerRunWorkspaces_OfFinishedRuns_AreRemoved()
    {
        var thread = Guid.NewGuid();
        var finished = await SeedRunAsync(thread, CliRunStatus.Completed, "x");
        var executing = await SeedRunAsync(thread, CliRunStatus.Running, "y");
        var finishedRoot = CreateRunRoot("host", finished);
        var executingRoot = CreateRunRoot("host", executing);

        await HandleAsync(thread);

        Directory.Exists(finishedRoot).ShouldBeFalse();
        Directory.Exists(executingRoot).ShouldBeTrue();
    }

    [Fact]
    public async Task UserProvidedWorkDirectory_OnlyTheFrameworksOwnOutputAndLogsAreRemoved()
    {
        var thread = Guid.NewGuid();
        var run = await SeedRunAsync(thread, CliRunStatus.Completed, "x");
        var runRoot = CreateRunRoot("host", run, userOwned: true);

        await HandleAsync(thread);

        Directory.Exists(Path.Combine(runRoot, CliWorkspaceLayout.OutputDirectoryName)).ShouldBeFalse();
        Directory.Exists(Path.Combine(runRoot, CliWorkspaceLayout.LogDirectoryName)).ShouldBeFalse();
        File.Exists(Path.Combine(runRoot, CliWorkspaceLayout.GcMetadataFileName)).ShouldBeTrue("same rule as the workspace GC");
    }

    [Fact]
    public async Task RunningItAgain_IsSafe_AndStillRemovesAWorkspaceLeftByAnEarlierAttempt()
    {
        // 总线重试整个处理器：上一次删完了记录、在删目录时失败，这一次没有记录可找，目录照样要删
        var thread = Guid.NewGuid();
        await SeedRunAsync(thread, CliRunStatus.Completed, "x");
        await HandleAsync(thread);
        var leftOver = CreateRunRoot("host", thread);

        await HandleAsync(thread);

        (await RunsAsync()).ShouldBeEmpty();
        Directory.Exists(leftOver).ShouldBeFalse();
    }

    // ── 会话记录（ProvidedConfigDirectory）────────────────────────────────

    /// <summary>
    /// 使用方给出配置目录与项目目录名的那一档：删除线程时，按每一轮的会话 id 在执行目标上删掉 CLI 的会话记录，
    /// 同一项目里别的会话不动。配置目录与项目目录名是删除时再问一次启动档案得到的。
    /// </summary>
    [Fact]
    public async Task ProvidedConfigDirectory_TheThreadsSessionRecordsAreDeletedOnTheTarget()
    {
        var thread = Guid.NewGuid();
        await SeedRunAsync(thread, CliRunStatus.Completed, "x", sessionId: "sess-1");
        await SeedRunAsync(thread, CliRunStatus.Failed, "y", sessionId: "sess-2");
        var host = await SessionHostAsync("sess-1", "sess-2", "sess-other");

        await HandleAsync(thread, host, ProvidedClaude());

        var files = ((InMemoryHostFileSystem)host.FileSystem).Files;
        files.ShouldNotContain("/home/u/.claude/projects/demo/sess-1.jsonl");
        files.ShouldNotContain("/home/u/.claude/projects/demo/sess-2.jsonl");
        files.ShouldNotContain("/home/u/.claude/projects/demo/sess-1/subagents/a.jsonl", "the session's own directory goes too");
        files.ShouldContain("/home/u/.claude/projects/demo/sess-other.jsonl", "another conversation in the same project stays");
        var request = host.ProfileRequests.ShouldHaveSingleItem();
        request.Purpose.ShouldBe(CliLaunchProfilePurpose.SessionCleanup);
        request.Scope.ThreadId.ShouldBe(thread);
        (await RunsAsync()).ShouldBeEmpty();
    }

    /// <summary>执行目标连不上（用户的容器没起来）：记录留到 CLI 自己按保留期清，库里的内容照删，不等它。</summary>
    [Fact]
    public async Task ProvidedConfigDirectory_AnUnreachableTarget_LeavesTheRecordsButNotTheRows()
    {
        var thread = Guid.NewGuid();
        await SeedRunAsync(thread, CliRunStatus.Completed, "x", sessionId: "sess-1");
        var host = await SessionHostAsync("sess-1");
        host.UnavailableWith = new CliHostUnavailableException("The workspace agent for this user is not connected.");

        await HandleAsync(thread, host, ProvidedClaude());

        ((InMemoryHostFileSystem)host.FileSystem).Files.ShouldContain("/home/u/.claude/projects/demo/sess-1.jsonl");
        (await RunsAsync()).ShouldBeEmpty();
    }

    /// <summary>别的档位不问执行目标：记录要么随工作区走，要么框架找不到它们，不去猜。</summary>
    [Fact]
    public async Task OtherIsolationTiers_DoNotAskTheTarget()
    {
        var thread = Guid.NewGuid();
        await SeedRunAsync(thread, CliRunStatus.Completed, "x", sessionId: "sess-1");
        var host = await SessionHostAsync("sess-1");

        await HandleAsync(thread, host, CliBuiltInProviders.All["claude"]);

        host.Scopes.ShouldBeEmpty();
        ((InMemoryHostFileSystem)host.FileSystem).Files.ShouldContain("/home/u/.claude/projects/demo/sess-1.jsonl");
    }

    /// <summary>会话 id 来自 CLI 的输出：带路径成分的一律不拿去拼路径。</summary>
    [Fact]
    public async Task ProvidedConfigDirectory_ASessionIdWithAPathInIt_DeletesNothing()
    {
        var thread = Guid.NewGuid();
        await SeedRunAsync(thread, CliRunStatus.Completed, "x", sessionId: "../../settings");
        var host = await SessionHostAsync("sess-1");
        await host.FileSystem.WriteAllTextAsync("/home/u/.claude/settings.json", "{}", CancellationToken.None);

        await HandleAsync(thread, host, ProvidedClaude());

        ((InMemoryHostFileSystem)host.FileSystem).Files.ShouldContain("/home/u/.claude/settings.json");
    }

    private static CliProviderDescriptor ProvidedClaude()
        => CliBuiltInProviders.All["claude"] with { UserConfigIsolation = CliUserConfigIsolation.ProvidedConfigDirectory };

    /// <summary>一个远程目标：配置目录 <c>/home/u/.claude</c>、项目 <c>demo</c> 下有这些会话的记录。</summary>
    private async Task<TestCliHost> SessionHostAsync(params string[] sessions)
    {
        DbContext.Set<CliRuntime>().Add(new CliRuntime
        {
            Id = RuntimeId, HostId = "TEST-HOST", ProviderKey = "claude", Name = "claude", Status = CliRuntimeStatus.Online
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var host = new TestCliHost((_, _) => throw new InvalidOperationException("not started"))
        {
            LaunchProfile = _ => new CliLaunchProfile { ConfigDirectory = "/home/u/.claude", ProjectDirectoryName = "demo" }
        };
        var fileSystem = host.FileSystem;
        await fileSystem.CreateDirectoryAsync("/home/u/.claude/projects/demo", CancellationToken.None);
        foreach (var session in sessions)
        {
            await fileSystem.WriteAllTextAsync($"/home/u/.claude/projects/demo/{session}.jsonl", "{}", CancellationToken.None);
            await fileSystem.CreateDirectoryAsync($"/home/u/.claude/projects/demo/{session}/subagents", CancellationToken.None);
            await fileSystem.WriteAllTextAsync($"/home/u/.claude/projects/demo/{session}/subagents/a.jsonl", "{}", CancellationToken.None);
        }

        return host;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task HandleAsync(Guid threadId, ICliHost? host = null, CliProviderDescriptor? provider = null)
    {
        using var scope = ServiceProvider.CreateScope();
        var registry = new Mock<ICliProviderRegistry>();
        if (provider is not null)
        {
            registry.Setup(r => r.Find(provider.Key)).Returns(provider);
        }

        var handler = new CliThreadCleanupHandler(
            scope.ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>(),
            scope.ServiceProvider.GetRequiredService<IRepository<CliRunMessage, Guid>>(),
            _registry,
            new CliRunSignalHub(),
            Options(),
            NullLogger<CliThreadCleanupHandler>.Instance,
            host,
            registry.Object,
            scope.ServiceProvider.GetRequiredService<IRepository<CliRuntime, Guid>>());

        await handler.HandleAsync(new ThreadDeletedEvent { ThreadId = threadId });
    }

    private IOptionsMonitor<CliAgentOptions> Options()
    {
        var monitor = new Mock<IOptionsMonitor<CliAgentOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(new CliAgentOptions { Enabled = true, WorkspacesRoot = _workspacesRoot });
        return monitor.Object;
    }

    private async Task<Guid> SeedRunAsync(
        Guid? threadId, CliRunStatus status, string prompt, string? output = null, string? perTurnContext = null,
        string? sessionId = null)
    {
        var run = new CliRun
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            ThreadId = threadId,
            Status = status,
            Prompt = prompt,
            PerTurnContext = perTurnContext,
            Output = output,
            ProviderSessionId = sessionId,
            WorkDirectory = sessionId is null ? null : "/home/u/sessions/s1/demo",
            CompletedAt = sessionId is null ? null : DateTime.UtcNow
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return run.Id;
    }

    private async Task SeedMessagesAsync(Guid runId, params string[] contents)
    {
        var sequence = 0;
        foreach (var content in contents)
        {
            DbContext.Set<CliRunMessage>().Add(new CliRunMessage
            {
                RunId = runId,
                Sequence = ++sequence,
                Type = CliAgentEventType.ToolResult,
                Content = content,
                Output = content
            });
        }

        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private async Task<List<CliRun>> RunsAsync()
        => await DbContext.Set<CliRun>().AsNoTracking().IgnoreQueryFilters().ToListAsync();

    private async Task<List<CliRunMessage>> MessagesAsync()
        => await DbContext.Set<CliRunMessage>().AsNoTracking().IgnoreQueryFilters().ToListAsync();

    /// <summary>按布置器的形状建一个运行根：工作目录、产物、日志与回收元数据。</summary>
    private string CreateRunRoot(string parent, Guid owner, bool userOwned = false)
    {
        var root = Path.Combine(_workspacesRoot, parent, owner.ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, CliWorkspaceLayout.WorkDirectoryName));
        Directory.CreateDirectory(Path.Combine(root, CliWorkspaceLayout.OutputDirectoryName));
        Directory.CreateDirectory(Path.Combine(root, CliWorkspaceLayout.LogDirectoryName));
        File.WriteAllText(Path.Combine(root, CliWorkspaceLayout.WorkDirectoryName, "notes.txt"), "content from the conversation");
        File.WriteAllText(
            Path.Combine(root, CliWorkspaceLayout.GcMetadataFileName),
            JsonSerializer.Serialize(new CliWorkspaceGcMetadata
            {
                RunId = owner,
                CreatedAt = DateTime.UtcNow,
                UserOwnedWorkDirectory = userOwned
            }));
        return root;
    }
}
