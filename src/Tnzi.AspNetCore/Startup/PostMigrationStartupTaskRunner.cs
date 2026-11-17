namespace Tnzi.AspNetCore.Startup;

/// <summary>
/// 一个迁移后启动任务走到哪一步了。不可变：每次运行产生一份新的，<see cref="PostMigrationStartupTaskRunner.States"/> 整体替换。
/// </summary>
/// <param name="Task">任务本身。</param>
/// <param name="Completed">是否已经成功完成过一次（完成之后不再运行，直到下次启动）。</param>
/// <param name="FailedAttempts">自启动以来失败了几次（完成之后归零）。</param>
/// <param name="FailingSince">第一次失败的时刻；没有失败时为 null。</param>
/// <param name="LastFailure">最近一次失败的异常；没有失败时为 null。</param>
internal sealed record PostMigrationStartupTaskState(
    IPostMigrationStartupTask Task,
    bool Completed,
    int FailedAttempts,
    DateTimeOffset? FailingSince,
    Exception? LastFailure)
{
    public string Name => Task.GetType().Name;

    /// <summary>失败过、还没有完成：后台在重试，健康检查报告它。</summary>
    public bool Failing => !Completed && FailedAttempts > 0;
}

/// <summary>
/// 运行全部 <see cref="IPostMigrationStartupTask"/>，记下每一个的结果，并只重跑还没完成的那些。
/// </summary>
/// <remarks>
/// 启动时 <see cref="RunAllAsync"/> 按注册顺序各跑一次，失败只记日志、不打断启动（与以前一样）；
/// 不同的是失败被记住了：<see cref="PostMigrationStartupTaskRetryService"/> 在后台调 <see cref="RetryIncompleteAsync"/>
/// 直到每个任务都完成过一次，<see cref="PostMigrationStartupTaskHealthCheck"/> 在那之前报告它们。
/// 没有这两样，启动那一刻数据库不可达的任务就一直没做，直到下次重启，只留下一条 Error。
/// 同一时刻只有一轮在跑（启动那一轮与后台重试不重叠）。
/// </remarks>
internal sealed class PostMigrationStartupTaskRunner(ILogger<PostMigrationStartupTaskRunner> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TaskCompletionSource _initialRun = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile IReadOnlyList<PostMigrationStartupTaskState>? _states;

    /// <summary>每个任务的当前状态；启动那一轮还没开始时为 null（宿主没有调 <c>RunPostMigrationStartupTasksAsync</c>）。</summary>
    public IReadOnlyList<PostMigrationStartupTaskState>? States => _states;

    /// <summary>启动那一轮跑完时完成。</summary>
    public Task InitialRunCompleted => _initialRun.Task;

    /// <summary>是否还有失败过、没有完成的任务。</summary>
    public bool HasIncomplete => _states?.Any(s => !s.Completed) == true;

    /// <summary>启动那一轮：每个任务各跑一次。</summary>
    public async Task RunAllAsync(
        IReadOnlyList<IPostMigrationStartupTask> tasks,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(tasks);
        Check.NotNull(serviceProvider);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _states = tasks.Select(t => new PostMigrationStartupTaskState(t, false, 0, null, null)).ToArray();
            await RunPendingAsync(serviceProvider, isRetry: false, cancellationToken);
        }
        finally
        {
            _gate.Release();
            _initialRun.TrySetResult();
        }
    }

    /// <summary>重跑还没完成的任务；全部完成时返回 <see langword="true"/>。</summary>
    public async Task<bool> RetryIncompleteAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        Check.NotNull(serviceProvider);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await RunPendingAsync(serviceProvider, isRetry: true, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        return !HasIncomplete;
    }

    private async Task RunPendingAsync(IServiceProvider serviceProvider, bool isRetry, CancellationToken cancellationToken)
    {
        var states = _states ?? [];
        for (var i = 0; i < states.Count; i++)
        {
            if (states[i].Completed)
            {
                continue;
            }

            var next = await RunOneAsync(states[i], serviceProvider, isRetry, cancellationToken);
            states = states.Select((s, index) => index == i ? next : s).ToArray();
            _states = states;
        }
    }

    private async Task<PostMigrationStartupTaskState> RunOneAsync(
        PostMigrationStartupTaskState state,
        IServiceProvider serviceProvider,
        bool isRetry,
        CancellationToken cancellationToken)
    {
        try
        {
            await state.Task.ExecuteAsync(serviceProvider, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var failed = state with
            {
                FailedAttempts = state.FailedAttempts + 1,
                FailingSince = state.FailingSince ?? DateTimeOffset.UtcNow,
                LastFailure = ex,
            };

            if (isRetry)
            {
                logger.LogWarning(
                    "Post-migration startup task {TaskType} still fails ({Attempts} failed attempt(s)); it is retried again. {Error}",
                    state.Name, failed.FailedAttempts, ex.Message);
            }
            else
            {
                logger.LogError(ex,
                    "Post-migration startup task {TaskType} failed; startup continues and the task is retried in the background "
                    + "until it completes (health check 'startup-tasks' reports it meanwhile).",
                    state.Name);
            }

            return failed;
        }

        if (state.FailedAttempts > 0)
        {
            logger.LogInformation(
                "Post-migration startup task {TaskType} completed after {Attempts} failed attempt(s).",
                state.Name, state.FailedAttempts);
        }

        return state with { Completed = true, FailedAttempts = 0, FailingSince = null, LastFailure = null };
    }
}
