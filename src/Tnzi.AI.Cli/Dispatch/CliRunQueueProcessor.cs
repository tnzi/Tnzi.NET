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
    private readonly CliRunQueueSignal? _queueSignal;
    private readonly string _hostId = Environment.MachineName;

    /// <summary>已交给执行、还没收尾的运行（停机时等它们收尾，见 <see cref="DrainInFlightAsync"/>）。</summary>
    private readonly ConcurrentDictionary<Task, byte> _inFlight = new();

    /// <summary>初始化队列处理器。</summary>
    public CliRunQueueProcessor(
        IServiceScopeFactory scopeFactory,
        CliRunSignalHub signalHub,
        CliRunCancellationRegistry cancellationRegistry,
        IOptionsMonitor<CliAgentOptions> options,
        ILogger<CliRunQueueProcessor> logger,
        CliRunQueueSignal? queueSignal = null)
    {
        _scopeFactory = Check.NotNull(scopeFactory);
        _signalHub = Check.NotNull(signalHub);
        _cancellationRegistry = Check.NotNull(cancellationRegistry);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
        // 缺省只为直接构造（测试）：没有提示就只按轮询间隔醒来
        _queueSignal = queueSignal;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <c>AI:Cli:Enabled</c> 每轮重读，关着时循环常驻空转而不是退出：入队、绑定与探测端点都按当前值判断，
    /// 此前这里只在启动时读一次，关着启动就直接返回，运行期打开开关后入队答 200、运行却永远停在 Queued，
    /// 没有任何进程认领，也没有一条日志。运行期关掉则停止认领新运行，已在执行的跑完。
    /// 并发上限在第一次进入工作状态时定下，之后改它需要重启。
    /// </remarks>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        SemaphoreSlim? slots = null;
        bool? wasEnabled = null;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var options = _options.CurrentValue;
                    if (options.Enabled != wasEnabled)
                    {
                        _logger.LogInformation(options.Enabled
                            ? "External CLI agent execution is enabled; queue processor is claiming runs"
                            : "External CLI agent execution is disabled (AI:Cli:Enabled=false); queue processor idle");
                        wasEnabled = options.Enabled;
                    }

                    if (!options.Enabled)
                    {
                        await Task.Delay(options.PollInterval, stoppingToken);
                        continue;
                    }

                    slots ??= new SemaphoreSlim(options.MaxConcurrentRuns, options.MaxConcurrentRuns);

                    await ReclaimExpiredLeasesAsync(stoppingToken);
                    await RefreshCoverageAsync(stoppingToken);
                    await ActivateWaitingTargetsAsync(stoppingToken);

                    if (!await TryStartNextAsync(slots, stoppingToken))
                    {
                        await WaitForNextPollAsync(options.PollInterval, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // 队列循环本身绝不能死：一次数据库抖动（或运行期改出一份通不过校验的配置）不该让整个宿主从此不再认领任务。
                    _logger.LogError(ex, "CLI run queue loop iteration failed; retrying after the poll interval");
                    await SafeDelayAsync(DelayAfterFailure(), stoppingToken);
                }
            }
        }
        finally
        {
            // ★ 先等进行中的执行收尾，再释放并发槽。此前循环一退出就释放了槽、ExecuteAsync 随即返回：
            //   宿主不等进行中的执行就继续停机，执行器「停机就打回队列」那一步赶不上，运行停在 Dispatched / Running，
            //   只能等租约过期才被回收；执行随后在 finally 里归还已释放的槽，抛出没人观察的 ObjectDisposedException。
            //   BackgroundService.StopAsync 等 ExecuteAsync 的上界是 HostOptions.ShutdownTimeout，所以这里的等待同样受它约束。
            await DrainInFlightAsync();
            slots?.Dispose();
        }
    }

    /// <summary>
    /// 等所有已交出去的执行收尾。停机令牌已经触发，执行在收到取消之后各自打回队列或落终态。
    /// </summary>
    private async Task DrainInFlightAsync()
    {
        while (!_inFlight.IsEmpty)
        {
            var pending = _inFlight.Keys.ToArray();
            _logger.LogInformation("Waiting for {Count} in-flight CLI run(s) to stop", pending.Length);
            try
            {
                await Task.WhenAll(pending);
            }
            catch (Exception)
            {
                // 已在 Track 的延续里记过日志
            }
        }
    }

    /// <summary>登记一条已交出去的执行；收尾时自动摘掉，异常在这里记下（观察掉），不留给终结器。</summary>
    private void Track(Task execution)
    {
        _inFlight.TryAdd(execution, 0);
        execution.ContinueWith(
            task =>
            {
                _inFlight.TryRemove(task, out _);
                if (task.Exception is { } ex)
                {
                    _logger.LogError(ex.GetBaseException(), "A CLI run execution task faulted");
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>等一个轮询间隔，或者被提前叫醒（远程执行目标刚上线、刚入队了一条要唤起目标的运行）。</summary>
    private Task WaitForNextPollAsync(TimeSpan interval, CancellationToken stoppingToken)
        => _queueSignal is { } signal ? signal.WaitAsync(interval, stoppingToken) : Task.Delay(interval, stoppingToken);

    /// <summary>
    /// 失败后的等待间隔。配置本身通不过校验时读 <c>CurrentValue</c> 也会抛，退回默认间隔。
    /// </summary>
    private TimeSpan DelayAfterFailure()
    {
        try
        {
            return _options.CurrentValue.PollInterval;
        }
        catch (OptionsValidationException)
        {
            return new CliAgentOptions().PollInterval;
        }
    }

    /// <summary>
    /// 占一个并发槽、认领一条运行并交给执行。认领到并交出去了返回 true；没有可认领的返回 false。
    /// </summary>
    /// <remarks>
    /// 先占槽再认领：反过来会认领到超过并发上限的运行，然后卡在这里等槽，
    /// 而它们已经带着租约了，别的副本也拿不走。
    /// <para>
    /// ★ 槽只有两个合法去处：交给 <see cref="ExecuteClaimedAsync"/>（它在 finally 里归还），或者在这里归还。
    /// 认领查询抛异常（连接重置、命令超时、主从切换）也必须归还：此前只有「没认领到」这一个分支归还，
    /// 异常落进外层循环的通用 catch 就把槽带走了，累计 <c>MaxConcurrentRuns</c> 次数据库抖动之后
    /// 循环永久阻塞在等槽上，连同排在它前面的租约回收一起停摆；入队接口照样答 200，运行永远停在 Queued，
    /// 而此后一条日志都没有。
    /// </para>
    /// </remarks>
    internal async Task<bool> TryStartNextAsync(SemaphoreSlim slots, CancellationToken stoppingToken)
    {
        await slots.WaitAsync(stoppingToken);

        var handedOff = false;
        try
        {
            // 留在执行宿主上接着跑的进程先接：它们一直在花钱，而且占着线程，同线程的下一轮在等它们结束
            var runId = await TryClaimReattachAsync(stoppingToken) ?? await TryClaimAsync(stoppingToken);
            if (runId is null)
            {
                return false;
            }

            Track(ExecuteClaimedAsync(runId.Value, slots, stoppingToken));
            handedOff = true;
            return true;
        }
        finally
        {
            if (!handedOff)
            {
                slots.Release();
            }
        }
    }

    private async Task ExecuteClaimedAsync(Guid runId, SemaphoreSlim slots, CancellationToken stoppingToken)
    {
        using var cts = _cancellationRegistry.Register(runId, stoppingToken);
        var renewal = RenewLeaseLoopAsync(runId, cts.Token);

        try
        {
            await ExecuteRunAsync(runId, cts.Token);
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
    /// 执行一条已认领的运行，直到终态（或停机时打回队列）。
    /// </summary>
    /// <remarks><c>internal virtual</c> 只为测试能换成一个可控的执行（例如模拟收尾很慢的那种）。</remarks>
    internal virtual async Task ExecuteRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<CliRunExecutor>();
        await executor.ExecuteAsync(runId, cancellationToken);
    }

    /// <summary>
    /// 认领一条待执行运行。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 候选查询本身排除「同一 Thread 已有活跃运行」的行：
    /// 同一个对话线程并发跑两个 turn，会让两边看到彼此写了一半的工作目录。
    /// 这个互斥用查询表达而不是分布式锁：多一条谓词（翻译成 NOT EXISTS），少一套要维护的锁基础设施。
    /// </para>
    /// <para>
    /// ★ 互斥必须在 <c>Take</c> 之前：此前先取队首 10 条、再在内存里逐条跳过忙线程，
    /// 同一线程积压 10 条（长任务进行中连续追问，或者故意排满）就占满整个候选窗口，
    /// 所有用户、所有租户排在后面的运行再也不会被看到，空着的并发槽也用不上，而且没有任何错误。
    /// 认领前的那次逐条复查保留：它把「查询之后、条件更新之前」别处刚认领了同线程兄弟的窗口收得更窄。
    /// </para>
    /// <para>
    /// 运行时自己的并发上限（<see cref="Entities.CliRuntime.MaxConcurrentRuns"/>，管理端可改）同样在 <c>Take</c> 之前排除：
    /// 已派出与执行中的运行数到了上限的运行时，它排着的运行这一轮不出现在候选里，别的运行时的照常认领。
    /// 它与进程级的 <c>AI:Cli:MaxConcurrentRuns</c> 同时生效、取较小者；运行时行已不存在时不拦（交给执行器以「运行时不存在」判失败，
    /// 而不是让运行永远停在 Queued）。多副本同时认领同一运行时的最后一个名额时可能各认领一条、短暂超出上限，
    /// 这里刻意不像线程互斥那样认领后再让出：超出一条只是多占一份资源，线程互斥防的是两边写同一个工作区。
    /// 此前这一列只写不读，管理员把某运行时改成 1 想串行执行，保存成功、页面显示 1，宿主照样按全局上限并发。
    /// </para>
    /// <para>
    /// 全部查询走 <see cref="CliRunQueries.AcrossTenants"/>：这里没有当前租户，
    /// 带租户过滤的查询只看得见 <c>TenantId IS NULL</c> 的行，租户的运行会永远停在 Queued。
    /// 运行时是宿主级资源（不分租户），直接查即可。
    /// </para>
    /// </remarks>
    internal async Task<Guid?> TryClaimAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>();
        var runtimes = scope.ServiceProvider.GetRequiredService<IRepository<Entities.CliRuntime, Guid>>().AsQueryable();
        var options = _options.CurrentValue;
        var coverage = CoverageOf(scope.ServiceProvider);
        var inProcess = coverage.InProcessRuntimes;
        var targets = coverage.RemoteTargets.ToList();

        var now = DateTime.UtcNow;
        var runs = repository.AcrossTenants();
        var candidates = await runs
            .Where(r => r.Status == CliRunStatus.Queued && !r.CancelRequested)
            // 只认领这个副本执行得了的：本机运行时上的（服务本机时），以及它此刻服务的远程执行目标上的。
            // 运行时行已不存在的照旧由服务本机的副本认领，交给执行器以「运行时不存在」判失败
            .Where(r => runtimes.Any(rt => rt.Id == r.CliRuntimeId
                                           && ((rt.Mode == CliRuntimeMode.InProcess && inProcess)
                                               || (rt.Mode == CliRuntimeMode.RemoteDaemon && targets.Contains(rt.HostId))))
                        || (inProcess && !runtimes.Any(rt => rt.Id == r.CliRuntimeId)))
            .Where(r => r.ThreadId == null
                        || !runs.Any(o => o.ThreadId == r.ThreadId
                                          && o.Id != r.Id
                                          && (o.Status == CliRunStatus.Dispatched || o.Status == CliRunStatus.Running)))
            .Where(r => !runtimes.Any(rt => rt.Id == r.CliRuntimeId
                                            && runs.Count(o => o.CliRuntimeId == rt.Id
                                                               && (o.Status == CliRunStatus.Dispatched
                                                                   || o.Status == CliRunStatus.Running))
                                               >= rt.MaxConcurrentRuns))
            .OrderByDescending(r => r.Priority)
            .ThenBy(r => r.CreationTime)
            .Take(10)
            .Select(r => new { r.Id, r.AgentId, r.ThreadId })
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            if (candidate.ThreadId is { } threadId)
            {
                var threadBusy = await repository.AcrossTenants().AnyAsync(
                    r => r.ThreadId == threadId
                         && r.Id != candidate.Id
                         && (r.Status == CliRunStatus.Dispatched || r.Status == CliRunStatus.Running),
                    cancellationToken);

                if (threadBusy)
                {
                    continue;
                }
            }

            var claimed = await repository.AcrossTenants()
                .Where(r => r.Id == candidate.Id && r.Status == CliRunStatus.Queued)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, CliRunStatus.Dispatched)
                    .SetProperty(r => r.DispatchedAt, now)
                    .SetProperty(r => r.ClaimedByHostId, _hostId)
                    .SetProperty(r => r.LeaseExpiresAt, now.Add(options.LeaseDuration)), cancellationToken);

            if (claimed == 0)
            {
                continue;
            }

            // 认领之后再看一次线程：复查与条件更新是两条语句，两个副本可以各自在对方写入之前复查通过，
            // 各认领同一线程的一条运行，在同一个每线程工作区里并发执行。自己的认领已经落库之后，
            // 任何一方再看到同线程还有别的进行中运行就让出：先看的那一方看不到后到者、照常执行，
            // 后到者一定看得到先到者；两方都看到对方时都让出、下一轮再认领，从不会两边都跑。
            if (candidate.ThreadId is { } claimedThreadId
                && await YieldIfThreadTakenAsync(repository, candidate.Id, claimedThreadId, cancellationToken))
            {
                continue;
            }

            _logger.LogDebug("Claimed CLI run {RunId} on host {HostId}", candidate.Id, _hostId);
            return candidate.Id;
        }

        return null;
    }

    /// <summary>
    /// 认领一条等着接回进程的运行：它仍是 Running、带着执行宿主交出的句柄，而没有服务端在驱动它（租约已到期：停机时离开，或者驱动它的宿主崩溃了）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 认领是同样的条件更新（仍是 Running、仍带句柄、租约仍过期），受影响行数 0 = 别的副本先接走了。状态不变：
    /// 线程在整个断开期间一直算忙，同线程的下一轮不会被认领到同一个工作区里。
    /// </para>
    /// <para>
    /// 第一次被发现时记下断开的时间（停机离开时已经记过；宿主崩溃时就是租约到期的那一刻），
    /// <c>AI:Cli:Reattach:Timeout</c> 从这里算起，执行器在那之后还接不上就判 Interrupted。
    /// </para>
    /// </remarks>
    internal async Task<Guid?> TryClaimReattachAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>();
        var runtimes = scope.ServiceProvider.GetRequiredService<IRepository<Entities.CliRuntime, Guid>>().AsQueryable();
        var options = _options.CurrentValue;
        var now = DateTime.UtcNow;
        var coverage = CoverageOf(scope.ServiceProvider);
        var inProcess = coverage.InProcessRuntimes;
        var targets = coverage.RemoteTargets.ToList();
        var givenUpBefore = now - options.Reattach.Timeout;

        var candidates = await repository.AcrossTenants()
            .Where(r => r.Status == CliRunStatus.Running
                        && r.ReattachHandle != null
                        && r.LeaseExpiresAt != null
                        && r.LeaseExpiresAt < now)
            // 进程在哪个执行目标上，就由服务那个目标的副本接回来。没有副本服务它、又已经过了重新附着的时限的，
            // 任一副本都可以认领去把它判成中断：否则它会一直以 Running 占着线程，没有人来结束它
            .Where(r => runtimes.Any(rt => rt.Id == r.CliRuntimeId
                                           && ((rt.Mode == CliRuntimeMode.InProcess && inProcess)
                                               || (rt.Mode == CliRuntimeMode.RemoteDaemon && targets.Contains(rt.HostId))))
                        || (inProcess && !runtimes.Any(rt => rt.Id == r.CliRuntimeId))
                        || (r.DetachedAt ?? r.LeaseExpiresAt) < givenUpBefore)
            .OrderBy(r => r.LeaseExpiresAt)
            .Take(10)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        foreach (var runId in candidates)
        {
            var claimed = await repository.AcrossTenants()
                .Where(r => r.Id == runId
                            && r.Status == CliRunStatus.Running
                            && r.ReattachHandle != null
                            && r.LeaseExpiresAt != null
                            && r.LeaseExpiresAt < now)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.DetachedAt, r => r.DetachedAt ?? r.LeaseExpiresAt)
                    .SetProperty(r => r.ClaimedByHostId, _hostId)
                    .SetProperty(r => r.LeaseExpiresAt, now.Add(options.LeaseDuration)), cancellationToken);

            if (claimed > 0)
            {
                _logger.LogInformation("Claimed CLI run {RunId} on host {HostId} to reattach to its process", runId, _hostId);
                return runId;
            }
        }

        return null;
    }

    /// <summary>这个副本此刻执行得了哪些运行时上的运行；没有注册执行宿主（直接构造的测试）按本机。</summary>
    private static CliHostCoverage CoverageOf(IServiceProvider services)
        => services.GetService<ICliHost>()?.GetCoverage() ?? CliHostCoverage.InProcess;

    /// <summary>
    /// 把这个副本此刻服务的远程执行目标标成在线（刷新 <c>LastSeenAt</c>，清掉唤起中的状态）。
    /// </summary>
    /// <remarks>
    /// 「在线」由服务它的副本维持，而不是只靠使用方的登记与下线通知：副本崩溃时没有人来说「它下线了」，
    /// 不刷新就会在 <c>AI:Cli:Activation:OnlineTtl</c> 之后被判离线、被重新唤起（代理连回别的副本，那个副本接着刷新）。
    /// 刚刷过的不再写：每轮都写会让每个副本每个轮询间隔都改一遍它服务的所有行。
    /// </remarks>
    internal async Task RefreshCoverageAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var targets = CoverageOf(scope.ServiceProvider).RemoteTargets.ToList();
        if (targets.Count == 0)
        {
            return;
        }

        var options = _options.CurrentValue;
        var now = DateTime.UtcNow;
        var refreshedAfter = now - options.Activation.OnlineTtl / 3;
        var runtimes = scope.ServiceProvider.GetRequiredService<IRepository<Entities.CliRuntime, Guid>>();

        var refreshed = await runtimes.AsQueryable()
            .Where(rt => rt.Mode == CliRuntimeMode.RemoteDaemon
                         && targets.Contains(rt.HostId)
                         && rt.Status != CliRuntimeStatus.Disabled
                         && (rt.Status != CliRuntimeStatus.Online
                             || rt.LastSeenAt == null
                             || rt.LastSeenAt < refreshedAfter
                             || rt.ActivationStartedAt != null))
            .ExecuteUpdateAsync(s => s
                .SetProperty(rt => rt.LastSeenAt, now)
                .SetProperty(rt => rt.Status, CliRuntimeStatus.Online)
                .SetProperty(rt => rt.ActivationRequestedAt, (DateTime?)null)
                .SetProperty(rt => rt.ActivationStartedAt, (DateTime?)null), cancellationToken);

        if (refreshed > 0)
        {
            _logger.LogDebug("Host {HostId} refreshed {Count} remote CLI runtime(s) it serves", _hostId, refreshed);
        }
    }

    /// <summary>
    /// 远程执行目标上有运行在等、而没有副本服务它时：请使用方唤起它；等过了时限、或使用方拒绝，排在它上面的运行就此结束。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 「在等」= 排队中的运行，或者服务端重启后等着接回进程的运行。「没有副本服务它」= 运行时记录不是在线、或者
    /// <c>AI:Cli:Activation:OnlineTtl</c> 内没有副本刷新过它（见 <see cref="RefreshCoverageAsync"/>）。
    /// </para>
    /// <para>
    /// 唤起按运行时记录去重：一条条件更新（上一次请求已经过了 <c>RetryInterval</c>）写下这次请求的时间，受影响 1 行的副本才去调用使用方，
    /// 所以多个副本、每个轮询间隔都看到它，也只问一次。第一次请求的时间是这一轮等待的起点，过了 <c>Activation:Timeout</c> 还没上线，
    /// 排队中的运行以 <see cref="CliRunFailureReason.TargetUnavailable"/> 结束；等着接回进程的运行不在此列，它们按重新附着的时限处理。
    /// </para>
    /// <para>
    /// 远程运行时被停用时，排在它上面的运行在这里结束：本机运行时的这件事由执行器在认领之后做，而一个没人服务的远程目标上的运行永远不会被认领。
    /// </para>
    /// </remarks>
    internal async Task ActivateWaitingTargetsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var runRepository = services.GetRequiredService<IRepository<CliRun, Guid>>();
        var runtimeRepository = services.GetService<IRepository<Entities.CliRuntime, Guid>>();
        if (runtimeRepository is null)
        {
            return;
        }

        var options = _options.CurrentValue;
        var now = DateTime.UtcNow;
        var staleBefore = now - options.Activation.OnlineTtl;
        var runs = runRepository.AcrossTenants();

        var waiting = await runtimeRepository.AsQueryable()
            .Where(rt => rt.Mode == CliRuntimeMode.RemoteDaemon
                         && !(rt.Status == CliRuntimeStatus.Online && rt.LastSeenAt != null && rt.LastSeenAt >= staleBefore)
                         && runs.Any(r => r.CliRuntimeId == rt.Id
                                          && ((r.Status == CliRunStatus.Queued && !r.CancelRequested)
                                              || (r.Status == CliRunStatus.Running && r.ReattachHandle != null
                                                  && r.LeaseExpiresAt != null && r.LeaseExpiresAt < now))))
            .OrderBy(rt => rt.ActivationRequestedAt)
            .Take(20)
            .Select(rt => new { rt.Id, rt.HostId, rt.ProviderKey, rt.Status, rt.ActivationStartedAt })
            .ToListAsync(cancellationToken);

        foreach (var runtime in waiting)
        {
            if (runtime.Status == CliRuntimeStatus.Disabled)
            {
                await FailQueuedRunsAsync(runRepository, runtime.Id, CliRunFailureReason.Unknown,
                    "The bound external CLI runtime has been disabled by an administrator.", services);
                continue;
            }

            if (runtime.ActivationStartedAt is { } startedAt && now - startedAt >= options.Activation.Timeout)
            {
                await FailQueuedRunsAsync(runRepository, runtime.Id, CliRunFailureReason.TargetUnavailable,
                    $"The execution target '{runtime.HostId}' did not come online within {CliRunExecutor.FormatDuration(options.Activation.Timeout)}.",
                    services);
                await ResetActivationAsync(runtimeRepository, runtime.Id);
                continue;
            }

            var retryBefore = now - options.Activation.RetryInterval;
            var requested = await runtimeRepository.AsQueryable()
                .Where(rt => rt.Id == runtime.Id
                             && (rt.ActivationRequestedAt == null || rt.ActivationRequestedAt < retryBefore))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(rt => rt.ActivationRequestedAt, now)
                    .SetProperty(rt => rt.ActivationStartedAt, rt => rt.ActivationStartedAt ?? now), cancellationToken);
            if (requested == 0 || services.GetService<ICliTargetActivator>() is not { } activator)
            {
                continue;
            }

            // 读整行而不是投影：多租户关闭时 TenantId 不在模型里，投影它的查询翻译不了
            var first = await runs
                .Where(r => r.CliRuntimeId == runtime.Id && r.Status == CliRunStatus.Queued && !r.CancelRequested)
                .OrderByDescending(r => r.Priority)
                .ThenBy(r => r.CreationTime)
                .FirstOrDefaultAsync(cancellationToken);

            CliActivationResult result;
            try
            {
                result = await activator.ActivateAsync(new CliActivationRequest
                {
                    TargetId = runtime.HostId,
                    RuntimeId = runtime.Id,
                    ProviderKey = runtime.ProviderKey,
                    RunId = first?.Id,
                    TenantId = first?.TenantId,
                    UserId = first?.CreatorId,
                    ThreadId = first?.ThreadId
                }, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Could not ask for execution target {TargetId} to be started; trying again later", runtime.HostId);
                continue;
            }

            if (result.RefusalReason is { } reason)
            {
                _logger.LogInformation("Execution target {TargetId} will not be started: {Reason}", runtime.HostId, reason);
                await FailQueuedRunsAsync(runRepository, runtime.Id, CliRunFailureReason.TargetUnavailable, reason, services);
                await ResetActivationAsync(runtimeRepository, runtime.Id);
                continue;
            }

            _logger.LogInformation("Asked for execution target {TargetId} to be started for CLI run {RunId}", runtime.HostId, first?.Id);
        }
    }

    /// <summary>
    /// 把一个运行时上排队中的运行结束掉（执行目标起不来、运行时被停用）。逐条经 <see cref="CliRunFinalizer"/>：
    /// 每条各发一个结束事件，「仍在排队、仍在这个运行时上」在写入那一刻重新判定。
    /// </summary>
    private async Task FailQueuedRunsAsync(
        IRepository<CliRun, Guid> repository, Guid runtimeId, CliRunFailureReason reason, string message, IServiceProvider services)
    {
        var queued = await repository.AcrossTenants()
            .Where(r => r.CliRuntimeId == runtimeId && r.Status == CliRunStatus.Queued)
            .Select(r => r.Id)
            .ToListAsync(CancellationToken.None);
        if (queued.Count == 0)
        {
            return;
        }

        var finalizer = new CliRunFinalizer(repository, services.GetService<IEventBus>(), _logger,
            services.GetService<IRepository<CliRunPermissionRequest, Guid>>());
        var termination = new CliRunTermination(CliRunStatus.Failed, reason, message);
        foreach (var runId in queued)
        {
            if (await finalizer.FinishAsync(runId, termination, r => r.Status == CliRunStatus.Queued && r.CliRuntimeId == runtimeId))
            {
                _signalHub.Signal(runId);
            }
        }

        _logger.LogWarning("Ended {Count} queued CLI run(s) on runtime {RuntimeId}: {Message}", queued.Count, runtimeId, message);
    }

    /// <summary>这一轮等待结束（失败或拒绝）：下一条排进来的运行重新开始唤起与计时。</summary>
    private static Task ResetActivationAsync(IRepository<Entities.CliRuntime, Guid> repository, Guid runtimeId)
        => repository.AsQueryable()
            .Where(rt => rt.Id == runtimeId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(rt => rt.ActivationRequestedAt, (DateTime?)null)
                .SetProperty(rt => rt.ActivationStartedAt, (DateTime?)null), CancellationToken.None);

    /// <summary>
    /// 刚认领的运行所在线程上若还有别的进行中运行，就把这次认领原样退回队列。退回了返回 true。
    /// </summary>
    /// <remarks>
    /// 退回以「仍是这次认领」为条件（本宿主、仍是 Dispatched；已派出的行别人认领不了）：执行还没开始，什么都不用清理。
    /// </remarks>
    private async Task<bool> YieldIfThreadTakenAsync(
        IRepository<CliRun, Guid> repository, Guid runId, Guid threadId, CancellationToken cancellationToken)
    {
        var threadTaken = await repository.AcrossTenants().AnyAsync(
            r => r.ThreadId == threadId
                 && r.Id != runId
                 && (r.Status == CliRunStatus.Dispatched || r.Status == CliRunStatus.Running),
            cancellationToken);

        if (!threadTaken)
        {
            return false;
        }

        await repository.AcrossTenants()
            .Where(r => r.Id == runId
                        && r.Status == CliRunStatus.Dispatched
                        && r.ClaimedByHostId == _hostId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, CliRunStatus.Queued)
                .SetProperty(r => r.DispatchedAt, (DateTime?)null)
                .SetProperty(r => r.ClaimedByHostId, (string?)null)
                .SetProperty(r => r.LeaseExpiresAt, (DateTime?)null), cancellationToken);

        _logger.LogDebug(
            "Released CLI run {RunId}: another run of thread {ThreadId} was claimed at the same time", runId, threadId);
        return true;
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

                var updated = await repository.AcrossTenants()
                    .Where(r => r.Id == runId
                                && (r.Status == CliRunStatus.Dispatched || r.Status == CliRunStatus.Running))
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.LeaseExpiresAt, expiry), cancellationToken);

                if (updated == 0)
                {
                    return;
                }

                // 顺带看一眼取消标记：它可能由别的副本（或本副本的另一个请求）写下。
                var cancelRequested = await repository.AcrossTenants()
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
    /// <para>
    /// 宿主崩溃后没人续租，行会永远停在 Dispatched/Running。回收把它打回 Queued，
    /// 让别的副本接手。这是整套租约机制存在的唯一理由。
    /// </para>
    /// <para>
    /// 已经被要求取消的行例外：它的宿主死了，没人能把取消落成终态，而打回 Queued 会让它以
    /// <c>Queued + CancelRequested</c> 的形态永远躺在队列里 —— 认领谓词跳过它，取消端点又只处理
    /// 读到时还是 Queued 的行，SSE 永远等不到终态。这里是「Queued」的第二个写入者，
    /// 与执行器停机时的 <c>ReleaseToQueueAsync</c> 同形，所以同样改判 <c>Cancelled</c>。
    /// 判终态逐条经 <see cref="CliRunFinalizer"/>：每条运行各发一个结束事件，前置条件（租约仍过期、仍在进行中、仍要求取消）
    /// 在写入那一刻重新判定，读出名单之后它的宿主续上了租约、或者自己先落了终态，这里就不再碰它。
    /// </para>
    /// </remarks>
    internal async Task ReclaimExpiredLeasesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>();
        var now = DateTime.UtcNow;

        // 带句柄的 Running 行不在此列：它的进程还在执行宿主上跑，由 TryClaimReattachAsync 接回来（取消请求也在接回来之后落实，
        // 那时才停得掉进程）。打回 Queued 会让它从头再跑一遍，同线程的下一轮也会被认领到它还在用的工作区里
        var expired = repository.AcrossTenants()
            .Where(r => r.LeaseExpiresAt != null
                        && r.LeaseExpiresAt < now
                        && (r.Status == CliRunStatus.Dispatched || (r.Status == CliRunStatus.Running && r.ReattachHandle == null)));

        var cancelRequested = await expired
            .Where(r => r.CancelRequested)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var cancelled = 0;
        if (cancelRequested.Count > 0)
        {
            var finalizer = new CliRunFinalizer(repository, scope.ServiceProvider.GetService<IEventBus>(), _logger,
                scope.ServiceProvider.GetService<IRepository<CliRunPermissionRequest, Guid>>());
            var termination = new CliRunTermination(CliRunStatus.Cancelled, CliRunFailureReason.Cancelled, "Execution cancelled.")
            {
                KeepExistingError = true
            };

            foreach (var runId in cancelRequested)
            {
                if (await finalizer.FinishAsync(runId, termination, r => r.LeaseExpiresAt != null
                        && r.LeaseExpiresAt < now
                        && r.CancelRequested
                        && (r.Status == CliRunStatus.Dispatched || r.Status == CliRunStatus.Running)))
                {
                    cancelled++;
                    _signalHub.Signal(runId);
                }
            }
        }

        var reclaimed = await expired
            .Where(r => !r.CancelRequested)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, CliRunStatus.Queued)
                .SetProperty(r => r.LeaseExpiresAt, (DateTime?)null)
                .SetProperty(r => r.ClaimedByHostId, (string?)null)
                .SetProperty(r => r.DispatchedAt, (DateTime?)null), cancellationToken);

        if (cancelled > 0)
        {
            _logger.LogWarning("Cancelled {Count} CLI run(s) whose lease expired after cancellation was requested", cancelled);
        }

        if (reclaimed > 0)
        {
            _logger.LogWarning("Reclaimed {Count} CLI run(s) whose lease expired", reclaimed);
        }
    }

    /// <summary>
    /// 执行器抛出时的兜底：把还没结束的运行判为 Failed / Unknown。
    /// </summary>
    /// <remarks>
    /// 经 <see cref="CliRunFinalizer"/> 写，只碰还没结束的运行：执行器常常是在写完终态之后、收尾时才抛出的
    /// （清理、删除已删线程的记录），那条已经写下（并且已经通知过）的结局不能被改判成 Failed。
    /// 此前条件是「不是 Completed」，一条已经 Cancelled 的运行会被改写成 Failed。
    /// </remarks>
    private async Task MarkFailedAsync(Guid runId, string message)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>();
            var finalizer = new CliRunFinalizer(repository, scope.ServiceProvider.GetService<IEventBus>(), _logger,
                scope.ServiceProvider.GetService<IRepository<CliRunPermissionRequest, Guid>>());
            await finalizer.FinishAsync(runId, new CliRunTermination(CliRunStatus.Failed, CliRunFailureReason.Unknown, message));

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
