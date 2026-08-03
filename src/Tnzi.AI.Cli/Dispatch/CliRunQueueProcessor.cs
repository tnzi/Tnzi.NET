namespace Tnzi.AI.Cli.Dispatch;

/// <summary>
/// 认领并执行队列里的外部运行。
/// </summary>
/// <remarks>
/// <para>
/// <b>认领必须是并发安全的，即使当前只有一个副本</b> —— 否则横向扩容的那一天，
/// 同一条运行会被两个进程同时执行，用户看到两份重复输出、账单翻倍，
/// 而这类故障只在生产的多副本下出现，本地永远测不出来。
/// </para>
/// <para>
/// 认领用<b>条件更新</b>（<c>WHERE Status = Queued</c>）而不是分布式锁，也不用
/// <c>FOR UPDATE SKIP LOCKED</c> —— 后者是 PostgreSQL 方言，违反数据库无关铁律。
/// 受影响行数 0 就是「被别人抢先了」，换下一条。
/// </para>
/// </remarks>
public class CliRunQueueProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CliRunSignalHub _signalHub;
    private readonly CliRunCancellationRegistry _cancellationRegistry;
    private readonly IOptionsMonitor<CliAgentOptions> _options;
    private readonly ILogger<CliRunQueueProcessor> _logger;
    private readonly string _hostId = Environment.MachineName;

    /// <summary>初始化队列处理器。</summary>
    public CliRunQueueProcessor(
        IServiceScopeFactory scopeFactory,
        CliRunSignalHub signalHub,
        CliRunCancellationRegistry cancellationRegistry,
        IOptionsMonitor<CliAgentOptions> options,
        ILogger<CliRunQueueProcessor> logger)
    {
        _scopeFactory = Check.NotNull(scopeFactory);
        _signalHub = Check.NotNull(signalHub);
        _cancellationRegistry = Check.NotNull(cancellationRegistry);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _options.CurrentValue;
        if (!options.Enabled)
        {
            _logger.LogInformation("External CLI agent execution is disabled (AI:Cli:Enabled=false); queue processor idle");
            return;
        }

        using var slots = new SemaphoreSlim(options.MaxConcurrentRuns, options.MaxConcurrentRuns);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReclaimExpiredLeasesAsync(stoppingToken);

                // 先占槽再认领：反过来会认领到超过并发上限的运行，然后卡在这里等槽 ——
                // 而它们已经带着租约了，别的副本也拿不走。
                await slots.WaitAsync(stoppingToken);
                var runId = await TryClaimAsync(stoppingToken);
                if (runId is null)
                {
                    slots.Release();
                    await Task.Delay(_options.CurrentValue.PollInterval, stoppingToken);
                    continue;
                }

                _ = ExecuteClaimedAsync(runId.Value, slots, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // 队列循环本身绝不能死：一次数据库抖动不该让整个宿主从此不再认领任务。
                _logger.LogError(ex, "CLI run queue loop iteration failed; retrying after the poll interval");
                await SafeDelayAsync(_options.CurrentValue.PollInterval, stoppingToken);
            }
        }
    }

    private async Task ExecuteClaimedAsync(Guid runId, SemaphoreSlim slots, CancellationToken stoppingToken)
    {
        using var cts = _cancellationRegistry.Register(runId, stoppingToken);
        var renewal = RenewLeaseLoopAsync(runId, cts.Token);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var executor = scope.ServiceProvider.GetRequiredService<CliRunExecutor>();
            await executor.ExecuteAsync(runId, cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Execution of CLI run {RunId} threw", runId);
            await MarkFailedAsync(runId, ex.Message);
        }
        finally
        {
            await cts.CancelAsync();
            await renewal;
            _cancellationRegistry.Unregister(runId);
            slots.Release();
        }
    }

    /// <summary>
    /// 认领一条待执行运行。
    /// </summary>
    /// <remarks>
    /// 候选查询里已经排除了「同一 Agent + 同一 Thread 已有活跃运行」的情形 ——
    /// 同一个对话线程并发跑两个 turn，会让两边看到彼此写了一半的工作目录。
    /// 这个互斥用查询表达而不是分布式锁：多一条谓词，少一套要维护的锁基础设施。
    /// </remarks>
    private async Task<Guid?> TryClaimAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>();
        var options = _options.CurrentValue;

        var now = DateTime.UtcNow;
        var candidates = await repository.AsQueryable()
            .Where(r => r.Status == CliRunStatus.Queued && !r.CancelRequested)
            .OrderByDescending(r => r.Priority)
            .ThenBy(r => r.CreationTime)
            .Take(10)
            .Select(r => new { r.Id, r.AgentId, r.ThreadId })
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            if (candidate.ThreadId is { } threadId)
            {
                var threadBusy = await repository.AsQueryable().AnyAsync(
                    r => r.ThreadId == threadId
                         && r.Id != candidate.Id
                         && (r.Status == CliRunStatus.Dispatched || r.Status == CliRunStatus.Running),
                    cancellationToken);

                if (threadBusy)
                {
                    continue;
                }
            }

            var claimed = await repository.AsQueryable()
                .Where(r => r.Id == candidate.Id && r.Status == CliRunStatus.Queued)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, CliRunStatus.Dispatched)
                    .SetProperty(r => r.DispatchedAt, now)
                    .SetProperty(r => r.ClaimedByHostId, _hostId)
                    .SetProperty(r => r.LeaseExpiresAt, now.Add(options.LeaseDuration)), cancellationToken);

            if (claimed > 0)
            {
                _logger.LogDebug("Claimed CLI run {RunId} on host {HostId}", candidate.Id, _hostId);
                return candidate.Id;
            }
        }

        return null;
    }

    /// <summary>
    /// 运行期间定期续租。
    /// </summary>
    /// <remarks>
    /// 续期间隔取租约的三分之一：一次数据库抖动导致的漏续不会立刻让租约过期，
    /// 而租约过期意味着别的副本会把这条正在跑的运行抢走并重跑一遍。
    /// </remarks>
    private async Task RenewLeaseLoopAsync(Guid runId, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var interval = TimeSpan.FromMilliseconds(Math.Max(1000, options.LeaseDuration.TotalMilliseconds / 3));

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!await SafeDelayAsync(interval, cancellationToken))
            {
                return;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>();
                var expiry = DateTime.UtcNow.Add(_options.CurrentValue.LeaseDuration);

                var updated = await repository.AsQueryable()
                    .Where(r => r.Id == runId
                                && (r.Status == CliRunStatus.Dispatched || r.Status == CliRunStatus.Running))
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.LeaseExpiresAt, expiry), cancellationToken);

                if (updated == 0)
                {
                    return;
                }

                // 顺带看一眼取消标记：它可能由别的副本（或本副本的另一个请求）写下。
                var cancelRequested = await repository.AsQueryable()
                    .AnyAsync(r => r.Id == runId && r.CancelRequested, cancellationToken);

                if (cancelRequested)
                {
                    _logger.LogInformation("Cancellation requested for CLI run {RunId}; aborting the process tree", runId);
                    _cancellationRegistry.TryCancel(runId);
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // 续期失败会让租约到期被回收，但那已经是安全的失败方向（重跑而不是丢任务）。
                _logger.LogWarning(ex, "Could not renew the lease for CLI run {RunId}", runId);
            }
        }
    }

    /// <summary>
    /// 回收租约过期的运行。
    /// </summary>
    /// <remarks>
    /// 宿主崩溃后没人续租，行会永远停在 Dispatched/Running。回收把它打回 Queued，
    /// 让别的副本接手。这是整套租约机制存在的唯一理由。
    /// </remarks>
    private async Task ReclaimExpiredLeasesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>();
        var now = DateTime.UtcNow;

        var reclaimed = await repository.AsQueryable()
            .Where(r => r.LeaseExpiresAt != null
                        && r.LeaseExpiresAt < now
                        && (r.Status == CliRunStatus.Dispatched || r.Status == CliRunStatus.Running))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, CliRunStatus.Queued)
                .SetProperty(r => r.LeaseExpiresAt, (DateTime?)null)
                .SetProperty(r => r.ClaimedByHostId, (string?)null)
                .SetProperty(r => r.DispatchedAt, (DateTime?)null), cancellationToken);

        if (reclaimed > 0)
        {
            _logger.LogWarning("Reclaimed {Count} CLI run(s) whose lease expired", reclaimed);
        }
    }

    private async Task MarkFailedAsync(Guid runId, string message)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>();
            await repository.AsQueryable()
                .Where(r => r.Id == runId && r.Status != CliRunStatus.Completed)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, CliRunStatus.Failed)
                    .SetProperty(r => r.FailureReason, (CliRunFailureReason?)CliRunFailureReason.Unknown)
                    .SetProperty(r => r.Error, message)
                    .SetProperty(r => r.CompletedAt, (DateTime?)DateTime.UtcNow)
                    .SetProperty(r => r.LeaseExpiresAt, (DateTime?)null), CancellationToken.None);

            _signalHub.Signal(runId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not mark CLI run {RunId} as failed", runId);
        }
    }

    private static async Task<bool> SafeDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
