using Microsoft.Data.Sqlite;

namespace Tnzi.AI.Tests;

/// <summary>
/// spawn 起的后台运行与它的运行行：行在后台任务起跑之前就已提交，后台任务异常结束时行不会停在「在跑」。
/// </summary>
/// <remarks>
/// <para>
/// 运行行此前在调用方作用域里插入。宿主开着 <c>EnableGlobalUnitOfWork</c> 时那是一个延迟到 action 结束才提交的插入，
/// 而 spawn_agent 所在的那一轮聊天要等整个 LLM 回合跑完；后台任务立刻在另一个作用域、另一条连接上按
/// <c>ExistingRunId</c> 找这一行，找不到就抛出、被吞成一条日志，子运行从不开始，行永远停在 Pending。
/// </para>
/// <para>
/// 后台任务在建立追踪之前就失败（解析不出 Agent、找不到行、起步前被取消）时，此前只记一条日志，
/// 行停在 Pending，并发上限一直把它算进去。
/// </para>
/// 真实的 SQLite 文件库：调用方与后台各用自己的连接，未提交的行跨连接不可见（与生产同形）。
/// </remarks>
public sealed class SubAgentSpawnPersistenceTests : IDisposable
{
    private static readonly TimeSpan BackgroundBound = TimeSpan.FromSeconds(30);

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"tnzi-spawn-{Guid.NewGuid():N}.db");
    private readonly ServiceProvider _root;
    private readonly Guid _agentId;
    private readonly BackgroundRuntime _runtime = new();

    public SubAgentSpawnPersistenceTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => NewContext());
        services.AddScoped<IRepository<AgentRun, Guid>>(sp => new EFCoreRepository<AgentRunFkDbContext, AgentRun, Guid>(sp.GetRequiredService<AgentRunFkDbContext>(), serviceProvider: sp));
        services.AddScoped<IRepository<AgentRunNode, Guid>>(sp => new EFCoreRepository<AgentRunFkDbContext, AgentRunNode, Guid>(sp.GetRequiredService<AgentRunFkDbContext>(), serviceProvider: sp));
        services.AddScoped<IRunStore, RunStore>();
        services.AddScoped<IAgentRuntime>(sp => new ScopedRuntime(_runtime, sp.GetRequiredService<IRunStore>()));
        _root = services.BuildServiceProvider();

        using var seed = NewContext();
        seed.Database.EnsureCreated();
        var agent = new Agent { Name = "worker", Provider = "OpenAI", Model = "gpt-4o", IsEnabled = true };
        seed.Set<Agent>().Add(agent);
        seed.SaveChangesAsync().GetAwaiter().GetResult();
        _agentId = agent.Id;
    }

    public void Dispose()
    {
        _root.Dispose();
        SqliteConnection.ClearAllPools();
        try { File.Delete(_databasePath); } catch (IOException) { }
    }

    private AgentRunFkDbContext NewContext()
    {
        var user = new Mock<ICurrentUser>();
        user.Setup(m => m.Id).Returns(Guid.Empty);
        return new AgentRunFkDbContext(
            new DbContextOptionsBuilder<AgentRunFkDbContext>().UseSqlite($"Data Source={_databasePath}").Options,
            user.Object);
    }

    [Fact]
    public async Task Spawn_InsideAnOpenCallerTransaction_TheBackgroundRunFindsItsRow()
    {
        using var caller = _root.CreateScope();
        var callerContext = caller.ServiceProvider.GetRequiredService<AgentRunFkDbContext>();
        // 宿主开着全局工作单元：这一轮聊天的事务要到整轮结束才提交。
        // 用延迟事务（不在 BEGIN 时就拿写锁）：SQLite 的写锁是整库的，生产库是行级的，别让测试替生产多锁一层
        var connection = (SqliteConnection)callerContext.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction(deferred: true);
        await callerContext.Database.UseTransactionAsync(transaction);

        var result = await CreateService(caller).SpawnAsync(new SpawnAgentRunInput { AgentId = _agentId, Message = "background work" });
        result.Succeeded.ShouldBeTrue(result.Message);

        var rowSeenByBackground = await _runtime.RowFound.Task.WaitAsync(BackgroundBound);
        rowSeenByBackground.ShouldBeTrue("the background run looks its row up on its own connection before the caller commits");
    }

    [Fact]
    public async Task Spawn_BackgroundFailsBeforeTracking_TheRowIsMarkedFailed()
    {
        _runtime.FailBeforeTracking = new InvalidOperationException("Agent resolution failed: provider is not configured");

        using var caller = _root.CreateScope();
        var result = await CreateService(caller).SpawnAsync(new SpawnAgentRunInput { AgentId = _agentId, Message = "background work" });
        result.Succeeded.ShouldBeTrue(result.Message);
        var runId = result.Data!.RunId;

        var row = await WaitForRowAsync(runId, r => r.Status != AgentRunStatus.Pending);
        row.Status.ShouldBe(AgentRunStatus.Failed);
        row.Error.ShouldNotBeNull().ShouldContain("provider is not configured");
    }

    [Fact]
    public async Task Spawn_RuntimeAlreadyRecordedAFinalState_IsLeftAlone()
    {
        _runtime.CompleteThenFail = true;

        using var caller = _root.CreateScope();
        var result = await CreateService(caller).SpawnAsync(new SpawnAgentRunInput { AgentId = _agentId, Message = "background work" });
        var runId = result.Data!.RunId;

        await _runtime.Finished.Task.WaitAsync(BackgroundBound);
        // 收尾是条件更新：运行时写下的 Completed 不会被改成 Failed
        await Task.Delay(200);
        var row = await WaitForRowAsync(runId, r => r.Status != AgentRunStatus.Pending);
        row.Status.ShouldBe(AgentRunStatus.Completed);
    }

    private SubAgentExecutionService CreateService(IServiceScope caller)
    {
        var resolver = new Mock<IAgentResolver>();
        resolver.Setup(x => x.ResolveAgentAsync(
                _agentId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentResolution.Success(Mock.Of<IAgentExecutor>(), "openai", "gpt-4o", _agentId, null, AgentExecutionMode.Single));

        return new SubAgentExecutionService(
            resolver.Object,
            caller.ServiceProvider.GetRequiredService<IRunStore>(),
            Mock.Of<ISubAgentRegistry>(),
            new AgentExecutionContextAccessor(),
            _root.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SubAgentExecutionService>.Instance,
            caller.ServiceProvider,
            new StaticOptionsMonitor<SubAgentOptions>(new SubAgentOptions()));
    }

    private async Task<AgentRun> WaitForRowAsync(Guid runId, Func<AgentRun, bool> condition)
    {
        var deadline = DateTime.UtcNow + BackgroundBound;
        while (true)
        {
            await using var context = NewContext();
            var row = await context.Set<AgentRun>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == runId);
            if (row != null && condition(row))
            {
                return row;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"Run {runId} never reached the expected state (last status: {row?.Status})");
            }

            await Task.Delay(20);
        }
    }

    /// <summary>后台运行时的行为开关与观测点（跨作用域共享）。</summary>
    private sealed class BackgroundRuntime
    {
        public Exception? FailBeforeTracking { get; set; }
        public bool CompleteThenFail { get; set; }
        public TaskCompletionSource<bool> RowFound { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>在后台作用域里像 RunTracker.GetOrCreateRunAsync 那样按 ExistingRunId 找行。</summary>
    private sealed class ScopedRuntime(BackgroundRuntime behaviour, IRunStore runStore) : IAgentRuntime
    {
        public async Task<AgentRunResult> RunAsync(AgentRunRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                if (behaviour.FailBeforeTracking is { } failure)
                {
                    throw failure;
                }

                var row = await runStore.GetAsync(request.ExistingRunId!.Value, cancellationToken);
                behaviour.RowFound.TrySetResult(row != null);

                if (behaviour.CompleteThenFail && row != null)
                {
                    row.Status = AgentRunStatus.Completed;
                    await runStore.UpdateAsync(row, cancellationToken);
                    throw new InvalidOperationException("post-completion hook failed");
                }

                return new AgentRunResult { Response = "ok", RunId = request.ExistingRunId, Status = AgentRunStatus.Completed };
            }
            finally
            {
                behaviour.Finished.TrySetResult();
            }
        }

        public IAsyncEnumerable<AgentStreamChunk> RunStreamingAsync(AgentRunRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AgentRunResult> ResumeAsync(Guid runId, ResumeRunInput? input = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
