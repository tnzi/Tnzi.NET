namespace Tnzi.AI.Cli.Dispatch;

/// <summary>
/// 执行一条已认领的外部运行：布置工作区 → 起进程 → 流式落库 → 回收。
/// </summary>
/// <remarks>
/// 每条运行一个 DI 作用域、一个实例。这里是唯一持有「进程 + 适配器 + 工作区」三者的地方，
/// 也因此是唯一必须保证它们都被收干净的地方 —— 无论中途从哪个分支退出。
/// </remarks>
public class CliRunExecutor
{
    private const int ToolOutputMaxLength = 32 * 1024;
    private const string CancelledMessage = "Execution cancelled.";
    private static readonly TimeSpan WatchdogTick = TimeSpan.FromSeconds(5);

    private readonly IRepository<CliRun, Guid> _runRepository;
    private readonly IRepository<CliRunMessage, Guid> _messageRepository;
    private readonly IRepository<CliAgentBinding, Guid> _bindingRepository;
    private readonly IRepository<Entities.CliRuntime, Guid> _runtimeRepository;
    private readonly IRepository<Agent, Guid> _agentRepository;
    private readonly ICliProviderRegistry _providerRegistry;
    private readonly ICliProtocolAdapterFactory _adapterFactory;
    private readonly ICliHost _host;
    private readonly ICliWorkspacePreparer _workspacePreparer;
    private readonly ICliBriefComposer _briefComposer;
    private readonly ICliMcpConfigComposer _mcpConfigComposer;
    private readonly CliRunTokenService _tokenService;
    private readonly ICliLaunchEnvironmentComposer _launchEnvironmentComposer;
    private readonly IAgentGrantService _grantService;
    private readonly ISkillService _skillService;
    private readonly CliRunSignalHub _signalHub;
    private readonly ICostCalculator? _costCalculator;
    private readonly IUsageLogService? _usageLogService;
    private readonly IBudgetService? _budgetService;
    private readonly IUsageLimitService? _usageLimits;
    private readonly IRepository<AgentThread, Guid>? _threadRepository;
    private readonly IOptionsMonitor<CliAgentOptions> _options;
    private readonly ICurrentTenant _currentTenant;
    private readonly ILogger<CliRunExecutor> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CliRunFinalizer _finalizer;
    private readonly ICliUsageLimitHandler? _usageLimitHandler;
    private readonly IReadOnlyList<ICliLaunchProfileContributor> _profileContributors;
    private readonly CliRunHandoffs? _handoffs;
    private readonly IEventBus? _eventBus;
    private readonly IHostApplicationLifetime? _applicationLifetime;

    // 这次运行在等后台任务、登记了可以把进程交给同线程的下一条运行（§6.18）；交出去的进程，收尾时（落终态之后）交给来取的那条
    private CliWaitingRun? _waiting;
    private CliLiveSession? _handoff;

    // 这次执行的启动档案：第一次起进程时问一次，续接被拒、换登录接着跑这些重试沿用它
    private CliLaunchProfile? _launchProfile;

    // 这次执行布置工作区时签发的回写凭据原文（它在这次起的进程的 MCP 配置里）：进程交出去时随它交给接手的运行
    private string? _writeBackToken;

    // 运行中途结束的那几轮里最后写进线程的回答：最后一轮什么都没说（等后台任务到了上限）时，运行记录上的回答指向它
    private Guid? _lastTurnMessageId;

    // 这次执行解析出的绑定、运行时、执行目标与身份：收尾时删会话记录要用
    private CliRunSetup? _setup;

    // 这次执行碰过的 CLI 会话（续接的那个与跑出来的那些）：线程在执行期间被删除时，按它们删会话记录
    private readonly HashSet<string> _sessionIds = new(StringComparer.Ordinal);

    /// <summary>初始化运行执行器。</summary>
    public CliRunExecutor(
        IRepository<CliRun, Guid> runRepository,
        IRepository<CliRunMessage, Guid> messageRepository,
        IRepository<CliAgentBinding, Guid> bindingRepository,
        IRepository<Entities.CliRuntime, Guid> runtimeRepository,
        IRepository<Agent, Guid> agentRepository,
        ICliProviderRegistry providerRegistry,
        ICliProtocolAdapterFactory adapterFactory,
        ICliHost host,
        ICliWorkspacePreparer workspacePreparer,
        ICliBriefComposer briefComposer,
        ICliMcpConfigComposer mcpConfigComposer,
        CliRunTokenService tokenService,
        ICliLaunchEnvironmentComposer launchEnvironmentComposer,
        IAgentGrantService grantService,
        ISkillService skillService,
        CliRunSignalHub signalHub,
        IOptionsMonitor<CliAgentOptions> options,
        ICurrentTenant currentTenant,
        ILogger<CliRunExecutor> logger,
        IServiceScopeFactory scopeFactory,
        ICostCalculator? costCalculator = null,
        IUsageLogService? usageLogService = null,
        IBudgetService? budgetService = null,
        IUsageLimitService? usageLimits = null,
        IRepository<AgentThread, Guid>? threadRepository = null,
        IEventBus? eventBus = null,
        ICliUsageLimitHandler? usageLimitHandler = null,
        IEnumerable<ICliLaunchProfileContributor>? profileContributors = null,
        IRepository<CliRunPermissionRequest, Guid>? permissionRepository = null,
        CliRunHandoffs? handoffs = null,
        IHostApplicationLifetime? applicationLifetime = null)
    {
        _runRepository = Check.NotNull(runRepository);
        _messageRepository = Check.NotNull(messageRepository);
        _bindingRepository = Check.NotNull(bindingRepository);
        _runtimeRepository = Check.NotNull(runtimeRepository);
        _agentRepository = Check.NotNull(agentRepository);
        _providerRegistry = Check.NotNull(providerRegistry);
        _adapterFactory = Check.NotNull(adapterFactory);
        _host = Check.NotNull(host);
        _workspacePreparer = Check.NotNull(workspacePreparer);
        _briefComposer = Check.NotNull(briefComposer);
        _mcpConfigComposer = Check.NotNull(mcpConfigComposer);
        _tokenService = Check.NotNull(tokenService);
        _launchEnvironmentComposer = Check.NotNull(launchEnvironmentComposer);
        _grantService = Check.NotNull(grantService);
        _skillService = Check.NotNull(skillService);
        _signalHub = Check.NotNull(signalHub);
        _options = Check.NotNull(options);
        _currentTenant = Check.NotNull(currentTenant);
        _logger = Check.NotNull(logger);
        _scopeFactory = Check.NotNull(scopeFactory);
        _costCalculator = costCalculator;
        _usageLogService = usageLogService;
        _budgetService = budgetService;
        _usageLimits = usageLimits;
        // 缺省只为直接构造（测试）：判不出线程是否已删除，就当没删（不删任何东西）
        _threadRepository = threadRepository;
        // 终态一律经它写：条件写入 + 完成转换的一方发布结束事件。总线缺省只为直接构造（测试），那时只写不发
        _finalizer = new CliRunFinalizer(runRepository, eventBus, logger, permissionRepository);
        _eventBus = eventBus;
        // 缺省：账号到了上限，这一轮就以 UsageLimit 结束（框架不知道还有别的账号）
        _usageLimitHandler = usageLimitHandler;
        // 缺省：档案就是执行目标给的那一份
        _profileContributors = profileContributors?.ToList() ?? [];
        // 缺省只为直接构造（测试）：等后台任务的运行不把进程交给下一条，下一条照旧排在等待后面
        _handoffs = handoffs;
        // 缺省只为直接构造（测试）：宿主是否在停机只看执行器的令牌
        _applicationLifetime = applicationLifetime;
    }

    /// <summary>执行一条运行，直到终态。</summary>
    /// <remarks>
    /// 运行记录跨租户加载（见 <see cref="CliRunQueries.AcrossTenants"/>），随后<b>整段执行都切到
    /// 它自己的租户</b>：绑定 / 运行时 / Agent / 授权 / 技能的查询与事件行的写入，都该在
    /// 派出这次运行的租户里发生。这个后台作用域本身没有租户，不切的话租户的运行会在
    /// 「Agent 已无绑定」上失败 —— 那条绑定明明在，只是在另一个租户的过滤器后面。
    /// </remarks>
    public async Task ExecuteAsync(Guid runId, CancellationToken cancellationToken)
    {
        // 不跟踪：这份实体只是本次执行的记账本，库里的每一次写都是按列的条件更新。
        // 被跟踪的话，作用域里任何一次 SaveChanges（用量记录、配额）都会把内存里的值无条件写回去，盖掉别的写入者（包括赢下终态的那一方）写下的事实。
        var run = await _runRepository.AcrossTenants()
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
        {
            _logger.LogWarning("Claimed CLI run {RunId} no longer exists", runId);
            return;
        }

        using var tenantScope = _currentTenant.Change(run.TenantId);

        // 服务端重启前起的进程还在执行宿主上跑（认领时它仍是 Running、带着句柄）：接回来，而不是从头再跑一遍
        var reattaching = run.Status == CliRunStatus.Running && !string.IsNullOrWhiteSpace(run.ReattachHandle);

        // 线程已经删除（删除事件处理器之后才被认领的运行，或入队与删除恰好赛跑）：一次都不跑，连同记录删掉。
        // 接回来的运行不在此列：它的进程已经在跑，照「运行期间线程被删」处理（跑完再删）
        if (!reattaching && await IsThreadDeletedAsync(run))
        {
            _logger.LogInformation("Discarding CLI run {RunId} without running it: thread {ThreadId} has been deleted", run.Id, run.ThreadId);
            await PurgeRunAsync(run.Id);
            _signalHub.Signal(runId);
            return;
        }

        ICliHostTarget? target = null;
        CliWorkspace? workspace = null;
        UsageHold? usage = null;
        var detached = false;
        var options = _options.CurrentValue;

        try
        {
            CliRunSetup? setup;
            CliAttempt attempt;
            if (reattaching)
            {
                // 预算门与用量上限门不再拦：进程一直在跑，花费照样发生，拦下它只会让这份花费不入账。
                // 结算照常：停机前那一次执行已经结算了它跑完的部分，这一次结算接回来的这一部分
                usage = ResumeUsageHold(run);

                // 过了重新附着的时限、而这个副本并不服务它所在的执行目标：它是作为「没人能接」的运行被认领的，就地判中断，不去碰宿主
                if (await AbandonedOnUnservedTargetAsync(run, options) is { } abandonedOn)
                {
                    await InterruptAsync(run,
                        $"The execution target '{abandonedOn}' did not come back within {FormatDuration(options.Reattach.Timeout)}.");
                    return;
                }

                setup = await ResolveSetupAsync(run, cancellationToken);
                if (setup is null)
                {
                    return;
                }

                target = setup.Target;
                _setup = setup;

                var reattached = await ReattachProcessAsync(run, setup, options);
                if (reattached is null)
                {
                    return;
                }

                workspace = await OpenWorkspaceAsync(run, setup);
                _signalHub.Signal(runId);

                // 断开期间有人要求取消：接回来就停下它（整树终止、落 Cancelled、已写出的部分留在线程里），不等下一次续租才发现
                using var cancelNow = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (await IsCancelRequestedAsync(run.Id))
                {
                    await cancelNow.CancelAsync();
                }

                attempt = await RunProcessAsync(
                    run, setup, workspace ?? UnmanagedWorkspace(run), options, usage, cancelNow.Token, reattached: reattached);
            }
            else
            {
                // 预算门与用量上限门排在 setup 解析之前：注定被拒的运行不该先跑掉 3~4 次查询去解析绑定与运行时。
                if (!await EnsureBudgetAsync(run, cancellationToken))
                {
                    return;
                }

                var begun = await BeginUsageAsync(run, cancellationToken);
                if (!begun.Allowed)
                {
                    return;
                }

                // 从这里起，无论从哪个分支退出，这一轮都在 finally 里结算一次
                usage = begun.Hold;

                setup = await ResolveSetupAsync(run, cancellationToken);
                if (setup is null)
                {
                    return;
                }

                // 从这里起这次运行的一切（工作区、进程、清理）都在同一个执行目标上
                target = setup.Target;
                _setup = setup;

                // 同线程的上一条运行在这个副本上等后台任务：接过它的进程（后台任务照常跑），这一轮写进同一个 stdin，而不是排在等待后面
                if (await TakeOverWaitingProcessAsync(run, setup, cancellationToken) is { } live)
                {
                    workspace = live.Workspace;
                    var continued = await ContinueOnProcessAsync(run, setup, live, options, usage, cancellationToken);
                    if (continued is null)
                    {
                        return;
                    }

                    attempt = continued;
                    if (attempt.ContinuationRefused)
                    {
                        // 进程接不了这一轮，已经结束：它的工作区像一轮结束时那样收尾，这条运行像平常起进程时一样布置自己的工作区
                        //（自己的回写凭据与 MCP 配置，执行目标与 contributor 按它重答启动档案），另起进程续接同一会话
                        run.ProviderSessionId = attempt.SessionId ?? live.SessionId;
                        run.MessageCount = attempt.Sequence;
                        await CleanupWorkspaceAsync(target, live.Workspace, removeAll: false);
                        workspace = null;
                        _launchProfile = null;

                        workspace = await PrepareWorkspaceAsync(run, setup, options, cancellationToken);
                        attempt = await RunProcessAsync(run, setup, workspace, options, usage, cancellationToken);
                    }
                }
                else
                {
                    await ResolveResumePointerAsync(run, setup, cancellationToken);

                    workspace = await PrepareWorkspaceAsync(run, setup, options, cancellationToken);

                    if (!await MarkRunningAsync(run, workspace, cancellationToken))
                    {
                        return;
                    }

                    _signalHub.Signal(runId);

                    attempt = await RunProcessAsync(run, setup, workspace, options, usage, cancellationToken);
                }
            }

            // 接回来的运行没认出工作区时，重试照样在那个目录里起进程，只是没有受管文件可以刷新
            var retryWorkspace = workspace ?? UnmanagedWorkspace(run);

            // 续接被拒:会话指针失效(CLI 侧归档过期、换了机器、目录被回收)。
            // 一次重试**开新会话**而不是把错误抛给用户 —— 用户问的是一个问题,
            // 不是要来管理会话指针的。上下文确实丢了,所以 PerTurnContext 里会明说,
            // 否则 agent 会自然地假装连续("如我之前所说…")而用户无从察觉。
            // ★ 被拒的那一次不落终态：运行在重试期间一直是 Running，终态只在最后一次写一回。
            // 先写 Failed 再重试，等结果的流式端点与门面看到终态就收尾了，重试跑出的答案没人接、不进线程却已计费，
            // 续租停了、取消端点答 409、回写凭据作废，而新会话指针照样存成下一轮的续接点。
            // 账号到了上限、消费方换好了另一份登录（§5.1.4）：同样不落终态，在这一次执行的会话里接着跑。
            // 两种重试各至多一次；接着跑的那次续接又被拒时，照上面的规则再开一次新会话。
            var resumeRetried = false;
            var limitRetried = false;
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!resumeRetried && attempt.Result.ResumeRejected)
                {
                    resumeRetried = true;
                    _logger.LogInformation(
                        "CLI run {RunId} had its resume rejected; retrying once with a fresh session", run.Id);

                    await PrepareFreshSessionRetryAsync(run, attempt);
                    var retry = await RunProcessAsync(run, setup, retryWorkspace, options, usage, cancellationToken);
                    attempt = retry with { Result = CombineWithEarlierAttempt(attempt.Result, retry.Result) };
                    continue;
                }

                if (!limitRetried && attempt.Result.FailureReason == CliRunFailureReason.UsageLimit
                    && await SignsInElsewhereAsync(run, setup, attempt, cancellationToken))
                {
                    limitRetried = true;
                    var continuation = await PrepareUsageLimitRetryAsync(run, attempt);
                    var retry = await RunProcessAsync(run, setup, retryWorkspace, options, usage, cancellationToken, continuation);
                    attempt = retry with { Result = CombineWithEarlierAttempt(attempt.Result, retry.Result) };
                    continue;
                }

                break;
            }

            _handoff = attempt.Handoff;
            await CompleteAsync(run, setup, attempt, cancellationToken);
        }
        catch (CliRunDetachedException)
        {
            // 宿主停机，而进程能在服务端重启后接回来：它留在执行宿主上接着跑，运行保持 Running，交给重启后的认领
            detached = true;
            await LeaveForReattachAsync(run);
        }
        catch (CliHostUnavailableException ex) when (reattaching && run.ReattachHandle is not null)
        {
            // 执行宿主眼下连不上（代理还没连回来）：稍后再接，到期限还接不上才算中断
            await RetryReattachLaterAsync(run, ex.Message, options);
        }
        catch (CliHostUnavailableException ex) when (IsHostStopping(cancellationToken))
        {
            // 服务端在停机：执行宿主连不上是停机本身造成的（消费方的远程通道随服务端一起关），不是这次运行的错。
            // 与停机时正在跑、接不回来的进程同样处置：打回队列交给下一个宿主；有人要求过取消则落取消。
            _logger.LogInformation(ex,
                "CLI run {RunId}: its execution host became unavailable while this server is shutting down; releasing the run to the queue", run.Id);
            if (await IsCancelRequestedAsync(run.Id))
            {
                await MarkCancelledAsync(run);
            }
            else
            {
                await ReleaseToQueueAsync(run);
            }
        }
        catch (CliExecutableNotFoundException ex)
        {
            await FailAsync(run, CliRunFailureReason.ExecutableNotFound, ex.Message, cancellationToken);
        }
        catch (CliProcessLaunchException ex)
        {
            await FailAsync(run, CliRunFailureReason.LaunchFailed, ex.Message, cancellationToken);
        }
        catch (CliHostUnavailableException ex)
        {
            await FailAsync(run, CliRunFailureReason.LaunchFailed, ex.Message, cancellationToken);
        }
        catch (CliProtocolNotImplementedException ex)
        {
            await FailAsync(run, CliRunFailureReason.LaunchFailed, ex.Message, cancellationToken);
        }
        catch (CliProviderConfigurationException ex)
        {
            await FailAsync(run, CliRunFailureReason.LaunchFailed, ex.Message, cancellationToken);
        }
        catch (CliWorkspaceClaimedException ex)
        {
            // 此前是通用 InvalidOperationException，落进队列处理器的兜底，以 Unknown 结束。
            await FailAsync(run, CliRunFailureReason.WorkspacePrepareFailed, ex.Message, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            await FailAsync(run, CliRunFailureReason.WorkspacePrepareFailed, ex.Message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 同一枚令牌背后有两种来源，处置相反：用户取消 → 终态 Cancelled；宿主停机 → 打回队列
            //（它一次都没跑完，别的副本还能接）。令牌本身分不清，持久化的取消标记才是判据。
            if (await IsCancelRequestedAsync(run.Id))
            {
                await MarkCancelledAsync(run);
            }
            else
            {
                await ReleaseToQueueAsync(run);
            }
        }
        finally
        {
            // 等后台任务的这条运行已经落了终态：进程交给来取它的那条运行（工作区随进程走，这里不收尾）；没能交出时进程结束、照常收尾
            var handedOver = await HandOverAsync(run);

            // 线程在这次运行期间被删除：删除事件的处理器删不得执行中的运行（见 CliThreadCleanupHandler），收尾由这里做。
            // 工作区整个删掉，运行记录与事件流（含处理器之后才写下的那些）一起删掉；用量已经按真实花费记过、配额照常结算。
            // 进程留在宿主上接着跑时，工作区与运行记录都还在用：收尾等接回来的那一次执行跑完再做。
            var threadDeleted = !detached && await IsThreadDeletedAsync(run);

            if (!detached && !handedOver && target is not null && workspace is not null)
            {
                await CleanupWorkspaceAsync(target, workspace, removeAll: threadDeleted);
            }

            // 会话记录在 CLI 的配置目录里（不随工作区走）：删除事件的处理器只删得了已经结束的运行的，这一次碰过的会话在这里删
            if (threadDeleted && target is not null)
            {
                await RemoveSessionRecordsAsync(run, target);
            }

            if (usage is not null)
            {
                await SettleUsageAsync(run, usage);
            }

            if (threadDeleted)
            {
                _logger.LogInformation("Removing CLI run {RunId}: thread {ThreadId} was deleted while it ran", run.Id, run.ThreadId);
                await PurgeRunAsync(run.Id);
            }

            _signalHub.Signal(runId);
        }
    }

    /// <summary>
    /// 同线程的上一条运行在这个副本上等后台任务时，等它在两轮之间停下、交出进程（§6.18）。没有这样的运行、或者它没能交出时返回 null（照常起进程）。
    /// </summary>
    /// <remarks>
    /// 只在第一次执行时取（重新执行的运行已经有自己的工作区与续接点）。等着的那条正在回答任务通知开出的一轮时等它答完：
    /// 两轮从不同时进行，这一轮也从不与它在同一个工作区里并发。
    /// </remarks>
    private async Task<CliLiveSession?> TakeOverWaitingProcessAsync(CliRun run, CliRunSetup setup, CancellationToken cancellationToken)
    {
        if (_handoffs is null || run.ThreadId is not { } threadId || run.StartedAt is not null
            || ResolveWorkDirectoryPolicy(run, setup).Mode == CliWorkDirectoryMode.PerRun)
        {
            return null;
        }

        var live = await _handoffs.TakeAsync(threadId, run.Id, cancellationToken);
        if (live is not null)
        {
            _logger.LogInformation(
                "CLI run {RunId} continues on the process of the previous run of thread {ThreadId}, which was waiting on background tasks",
                run.Id, threadId);
        }

        return live;
    }

    /// <summary>
    /// 在接过来的进程上跑这一轮：续接点就是那个进程的会话，工作区是它的工作区，回写凭据是它 MCP 配置里那一份。
    /// 进程接不了这一轮（启动方式不同、它的最后一轮恰好收尾）时返回 <see cref="CliAttempt.ContinuationRefused"/>（进程已结束），
    /// 由调用方布置这条运行自己的工作区、另起进程续接同一会话。不该再往下跑（已被取消）时返回 null，进程已结束。
    /// </summary>
    private async Task<CliAttempt?> ContinueOnProcessAsync(
        CliRun run, CliRunSetup setup, CliLiveSession live, CliAgentOptions options, UsageHold? usage, CancellationToken cancellationToken)
    {
        try
        {
            run.ProviderSessionId = live.SessionId;
            run.ResumeAtEntryId = null;
            run.ResumeExpected = !string.IsNullOrWhiteSpace(live.SessionId);

            if (!await MarkRunningAsync(run, live.Workspace, cancellationToken))
            {
                await live.EndAsync();
                return null;
            }

            _signalHub.Signal(run.Id);

            await TakeOverWriteBackChannelAsync(run, setup, live, options);
            return await RunProcessAsync(run, setup, live.Workspace, options, usage, cancellationToken, live: live);
        }
        catch when (!live.Adopted)
        {
            // 还没接手就出错（取消、写库失败）：进程没人管了，连同后台任务结束掉
            await live.EndAsync();
            throw;
        }
    }

    /// <summary>
    /// 接过进程时，在问启动档案、比较启动方式之前：进程 MCP 配置里那份回写凭据从此是这条运行的（哈希挪过来），
    /// 执行目标也像布置工作区时那样，经这条运行的 MCP 配置组合听到这条回写通道。
    /// </summary>
    /// <remarks>
    /// 执行目标可以从回写条目（<see cref="ICliHostTarget.CreateWriteBackServerEntry"/>）认出它在为哪条运行起进程，
    /// 并据此回答启动档案（例如按运行给的环境变量）。只在布置工作区时组合的话，接手的运行问到的档案缺了这些，
    /// 与进程起来时的启动方式永远对不上，进程（连同后台任务）每次都被结束，另起的进程也缺着它们。
    /// 交出它的那条已经落了终态（终态写入清掉了它那一行上的哈希），凭据只认进行中的运行，所以不会两条运行同时认它。
    /// 进程最终没接着用时，另起进程前布置的工作区签发这条运行自己的新凭据，挪过来的这份随之作废。
    /// </remarks>
    private async Task TakeOverWriteBackChannelAsync(CliRun run, CliRunSetup setup, CliLiveSession live, CliAgentOptions options)
    {
        if (live.WriteBackTokenHash is { } hash)
        {
            var expiresAt = live.WriteBackTokenExpiresAt;
            await _runRepository.AcrossTenants()
                .Where(r => r.Id == run.Id && r.Status == CliRunStatus.Running)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.WriteBackTokenHash, hash)
                    .SetProperty(r => r.WriteBackTokenExpiresAt, expiresAt), CancellationToken.None);
            run.WriteBackTokenHash = hash;
            run.WriteBackTokenExpiresAt = expiresAt;
        }

        if (options.WriteBack.Enabled && live.WriteBackToken is { } token)
        {
            _ = _mcpConfigComposer.Compose(setup.Binding.McpConfigJson, token, options.WriteBack, setup.Target);
        }
    }

    /// <summary>
    /// 这条运行接过进程、提示写进去之前：记下起进程的那条运行（<see cref="CliRun.ProcessLaunchedByRunId"/>），再同步发布
    /// <see cref="CliRunProcessHandedOverEvent"/>。按起进程的运行 id 授权、记账的消费方（模型网关）此后找得到这条运行，进程接下来的请求不会被拒。
    /// </summary>
    /// <remarks>
    /// 回写凭据在这之前已经挪到这条运行上（<see cref="TakeOverWriteBackChannelAsync"/>）。事件处理器的异常只记 Warning：持久化的链接才是权威答案。
    /// </remarks>
    private async Task TakeOverProcessAsync(CliRun run, CliLiveSession live)
    {
        var launchedBy = live.LaunchedByRunId;
        await _runRepository.AcrossTenants()
            .Where(r => r.Id == run.Id && r.Status == CliRunStatus.Running)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ProcessLaunchedByRunId, launchedBy), CancellationToken.None);
        run.ProcessLaunchedByRunId = launchedBy;

        if (_eventBus is null)
        {
            return;
        }

        try
        {
            await _eventBus.PublishAsync(new CliRunProcessHandedOverEvent
            {
                FromRunId = live.FromRunId,
                ToRunId = run.Id,
                LaunchedByRunId = launchedBy,
                AgentId = run.AgentId,
                ThreadId = run.ThreadId,
                UserId = run.CreatorId,
                TenantId = run.TenantId
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A handler of the handover of a CLI process to run {RunId} failed; the run goes on", run.Id);
        }
    }

    /// <summary>接过来的进程最终没接着用（这条运行另起了进程）：进程是这条运行自己的了，链接清掉。</summary>
    private async Task ReleaseProcessLinkAsync(CliRun run)
    {
        if (run.ProcessLaunchedByRunId is null)
        {
            return;
        }

        await _runRepository.AcrossTenants()
            .Where(r => r.Id == run.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ProcessLaunchedByRunId, (Guid?)null), CancellationToken.None);
        run.ProcessLaunchedByRunId = null;
    }

    /// <summary>
    /// 收尾时：这次运行登记过「在等后台任务」，就把结局交给来取进程的那条运行（进程，或者没有进程可交），连同并发槽。返回进程是否交出去了。
    /// </summary>
    /// <remarks>
    /// 只在落了终态之后交：来取的那条接着定续接点与用量基线，要看见这一条已经结束。没交出去（没人来取、来取的那条放弃了、
    /// 这条没能落终态）的进程在这里结束，后台任务随之结束。
    /// </remarks>
    private async Task<bool> HandOverAsync(CliRun run)
    {
        if (_waiting is not { } waiting)
        {
            return false;
        }

        _waiting = null;
        var session = _handoff;
        _handoff = null;

        // 没有进程可交（进程已经结束），或者这条没能落终态（来取的那条不能在它还算进行中时接着它的会话）：来取的那条另起进程
        var handed = session is not null && IsTerminal(run.Status) ? session : null;
        if (_handoffs!.Hand(waiting, handed) && handed is not null)
        {
            _logger.LogInformation("CLI run {RunId} handed its process and background tasks to run {SuccessorRunId}", run.Id, waiting.SuccessorRunId);
            return true;
        }

        if (session is not null)
        {
            _logger.LogInformation("CLI run {RunId} stopped to hand its process over, but it could not be handed (the next run gave up, or this run did not finish); ending it", run.Id);
            await session.EndAsync();
        }

        return false;
    }

    /// <summary>删掉一条运行与它的事件、附件（线程已删除时）。</summary>
    private async Task PurgeRunAsync(Guid runId)
    {
        using var scope = _scopeFactory.CreateScope();
        await CliRunPurge.DeleteRunAsync(
            _runRepository, _messageRepository, runId, CancellationToken.None,
            scope.ServiceProvider.GetService<IRepository<AgentAttachment, Guid>>(),
            scope.ServiceProvider.GetService<IRepository<CliRunPermissionRequest, Guid>>());
    }

    /// <summary>
    /// 这条运行所在的线程是否已被删除。没有线程、判不出来（没有线程仓储、查询失败）都回答 false：
    /// 那个方向的后果是内容多留一会儿，反过来是删错别人的运行记录。
    /// </summary>
    private async Task<bool> IsThreadDeletedAsync(CliRun run)
    {
        if (_threadRepository is null || run.ThreadId is not { } threadId)
        {
            return false;
        }

        try
        {
            return await CliRunPurge.IsThreadDeletedAsync(_threadRepository, threadId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // 收尾路径上不能因为这一次查询让工作区清理与配额结算一起跳过
            _logger.LogWarning(ex, "Could not tell whether thread {ThreadId} of CLI run {RunId} has been deleted", threadId, run.Id);
            return false;
        }
    }

    /// <summary>
    /// 认领之后、开进程之前的预算门。放行返回 true；超限则把运行判为 QuotaExceeded 并返回 false。
    /// </summary>
    /// <remarks>
    /// 入队时已经查过一次，这里再查是因为**队列可能积压很久**：期间别的运行会继续花钱，
    /// 入队那一刻的答案到认领时可能已经不成立。入队那次是为了让调用方当场收到反馈，
    /// 这一次才是真正拦住花费的那道。
    /// <para>
    /// 身份取自运行记录上持久化的 <c>TenantId</c>/<c>CreatorId</c>，不取环境上下文 ——
    /// 认领发生在后台服务里，那里既没有 HTTP 请求也没有 <c>ICurrentTenant</c>，
    /// 读环境只会得到 null，于是所有运行都会退化成「按全局预算算」，租户维度形同虚设。
    /// </para>
    /// </remarks>
    private async Task<bool> EnsureBudgetAsync(CliRun run, CancellationToken cancellationToken)
    {
        if (_budgetService is null) return true;

        var budget = await _budgetService.CheckBudgetAsync(
            run.CreatorId, run.TenantId, run.AgentId, cancellationToken);
        if (budget.IsAllowed) return true;

        _logger.LogWarning(
            "CLI run {RunId} rejected: budget exhausted (spend={Spend} limit={Limit})",
            run.Id, budget.CurrentSpendUsd, budget.BudgetLimitUsd);

        await FailAsync(run, CliRunFailureReason.QuotaExceeded,
            budget.Reason ?? "The AI cost budget is exhausted.", cancellationToken);
        return false;
    }

    /// <summary>
    /// 认领之后、开进程之前判按用户的用量上限，并开启还没开始的窗口（窗口从这一轮开始时起算）。
    /// </summary>
    /// <remarks>
    /// 内建路径上这件事长在 <c>QuotaMiddleware</c> 里：一轮开始前判上限，跑完按实际费用结算。外部执行按红线①不进管线，
    /// 不在这里补上，一个已经到上限的用户换一个绑了 CLI 运行时的 Agent 就能无限用下去，而且用量从不计入他的窗口。
    /// 身份取持久化的 <c>CreatorId</c>，理由同预算门；无用户时跳过，与中间件一致。
    /// <para>
    /// 入队时已经查过一次，这里再查是因为队列可能积压：期间别的运行会把窗口用到上限。到上限判
    /// <see cref="CliRunFailureReason.UserUsageLimit"/> 并记下窗口的重置时间；计量子系统自身失败同样不跑（失败关闭），
    /// 但判 <see cref="CliRunFailureReason.Unknown"/>，不说成「到上限了」。
    /// </para>
    /// </remarks>
    private async Task<(bool Allowed, UsageHold? Hold)> BeginUsageAsync(CliRun run, CancellationToken cancellationToken)
    {
        // 入队时落点就说了用量在别处计量的，不开窗口（CliRunPlacement.UsageMeteredElsewhere）
        if (_usageLimits is null || run.CreatorId is not { } userId || run.UsageMeteredElsewhere)
        {
            return (true, null);
        }

        var begun = await _usageLimits.BeginAsync(userId, cancellationToken);
        if (begun.Succeeded && begun.Data is not null)
        {
            return (true, new UsageHold(begun.Data));
        }

        var reached = string.Equals(begun.ErrorCode, ErrorCodes.UsageLimitReached, StringComparison.Ordinal) || begun.Code == 429;

        _logger.LogWarning(
            "CLI run {RunId} rejected: usage limit check for user {UserId} failed ({ErrorCode}): {Message}",
            run.Id, userId, begun.ErrorCode, begun.Message);

        if (reached)
        {
            var denial = begun.ErrorDetails as UsageLimitDenialDto;
            var finished = await _finalizer.FinishAsync(run.Id, new CliRunTermination(
                CliRunStatus.Failed, CliRunFailureReason.UserUsageLimit, begun.Message ?? "Usage limit reached.")
            {
                UsageLimitResetsAt = denial?.ResetsAt
            });
            run.Status = CliRunStatus.Failed;
            run.FailureReason = CliRunFailureReason.UserUsageLimit;
            run.Error = begun.Message;
            if (!finished)
            {
                _logger.LogWarning("CLI run {RunId} hit the usage limit here, but it had already been finished elsewhere", run.Id);
            }
        }
        else
        {
            await FailAsync(run, CliRunFailureReason.Unknown,
                begun.Message ?? "The usage limit could not be checked.", cancellationToken);
        }

        return (false, null);
    }

    /// <summary>
    /// 结算这一轮的费用：每条运行恰好一次，无论它从哪个分支结束。
    /// </summary>
    /// <remarks>
    /// 结算失败只记日志不抛：它跑在 finally 里，抛出会盖掉运行本身的结局（与 <c>QuotaMiddleware</c> 同一处理）。
    /// 用 <see cref="CancellationToken.None"/>：宿主停机时令牌已取消，带着它去写库结算会当场失败。
    /// </remarks>
    private async Task SettleUsageAsync(CliRun run, UsageHold hold)
    {
        try
        {
            var settled = await _usageLimits!.SettleAsync(hold.Reservation, hold.CostUsd, CancellationToken.None);
            if (!settled.Succeeded)
            {
                _logger.LogWarning(
                    "Failed to record the usage of CLI run {RunId} for user {UserId}: {Message}",
                    run.Id, hold.Reservation.UserId, settled.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to record the usage of CLI run {RunId} for user {UserId}", run.Id, hold.Reservation.UserId);
        }
    }

    private async Task<CliRunSetup?> ResolveSetupAsync(CliRun run, CancellationToken cancellationToken)
    {
        var binding = await _bindingRepository.FirstOrDefaultAsync(b => b.AgentId == run.AgentId, cancellationToken);
        if (binding is null)
        {
            await FailAsync(run, CliRunFailureReason.Unknown,
                "The agent no longer has an external CLI binding.", cancellationToken);
            return null;
        }

        // 运行时取运行记录上的那个：入队时由绑定或使用方的落点定下，认领也是按它决定由哪个副本执行的
        var runtime = await _runtimeRepository.GetAsync(run.CliRuntimeId, cancellationToken);
        if (runtime is null)
        {
            await FailAsync(run, CliRunFailureReason.Unknown,
                "The bound external CLI runtime no longer exists.", cancellationToken);
            return null;
        }

        // 入队时只拦了新入队的；管理员停用运行时（某个 CLI 版本出了问题）正是要已经排着的那些也别再跑。
        // 失败而不是留在队列里：一条永远认领不到的 Queued 行会让流式端点与门面一直等下去。
        if (runtime.Status == CliRuntimeStatus.Disabled)
        {
            await FailAsync(run, CliRunFailureReason.Unknown,
                "The bound external CLI runtime has been disabled by an administrator.", cancellationToken);
            return null;
        }

        var provider = _providerRegistry.Find(runtime.ProviderKey);
        if (provider is null || !provider.Enabled)
        {
            await FailAsync(run, CliRunFailureReason.Unknown,
                $"Provider '{runtime.ProviderKey}' is unknown or disabled in this deployment.", cancellationToken);
            return null;
        }

        if (!_adapterFactory.IsImplemented(provider.Protocol))
        {
            await FailAsync(run, CliRunFailureReason.LaunchFailed,
                $"No protocol adapter is implemented for '{provider.Protocol}' in this version.", cancellationToken);
            return null;
        }

        // 版本在部署声明的范围外不跑（范围没声明时任何版本都放行）。接回来的进程已经在跑，不拦
        if (run.Status != CliRunStatus.Running
            && CliVersionSupport.GetRejection(provider, runtime.CliVersion, runtime.HostId) is { } versionRejection)
        {
            await FailAsync(run, CliRunFailureReason.LaunchFailed, versionRejection, cancellationToken);
            return null;
        }

        // 这次运行在哪里执行：本机，或消费方的宿主按运行的身份选出的远程位置。取不到时抛 CliHostUnavailableException（LaunchFailed）。
        var scope = CreateScope(run, runtime, provider.Key);
        var target = await _host.GetTargetAsync(scope, cancellationToken);

        // 远程运行时上的运行必须落在它登记的那个执行目标上：宿主按作用域选错了容器，一个用户的运行就跑进了另一个用户的工作区
        if (runtime.Mode == CliRuntimeMode.RemoteDaemon && !string.Equals(target.TargetId, runtime.HostId, StringComparison.Ordinal))
        {
            await FailAsync(run, CliRunFailureReason.LaunchFailed,
                $"The execution host returned target '{target.TargetId ?? target.Name}' for a run placed on '{runtime.HostId}'.", cancellationToken);
            return null;
        }

        // 优先用注册时探测到的路径；它在执行目标上不在了（CLI 被卸载/升级换路径）就在那里现场重新解析。
        var executablePath = !string.IsNullOrWhiteSpace(runtime.ExecutablePath)
                             && await target.FileSystem.FileExistsAsync(runtime.ExecutablePath, cancellationToken)
            ? runtime.ExecutablePath
            : await target.FindExecutableAsync(provider, cancellationToken);

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            // 报执行这次运行的位置，而不是运行时登记时的宿主：认领不看运行时所在宿主，
            // 两者不同的时候，报登记宿主会让人去一台本来就装着 CLI 的机器上排查。
            var executingHost = target.Name;
            var registeredOn = string.Equals(runtime.HostId, executingHost, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : $" The bound runtime was registered on host '{runtime.HostId}'.";
            await FailAsync(run, CliRunFailureReason.ExecutableNotFound,
                $"Executable for provider '{provider.Key}' was not found on host '{executingHost}'.{registeredOn}", cancellationToken);
            return null;
        }

        var agent = await _agentRepository.GetAsync(run.AgentId, cancellationToken);
        if (agent is null)
        {
            await FailAsync(run, CliRunFailureReason.Unknown, "The agent no longer exists.", cancellationToken);
            return null;
        }

        // 入队时已拦过一次；这里再查是因为停用可能发生在排队期间，管理员按下停用，
        // 正是要已经排着的那些也别再跑（与内建路径 AgentResolver 同一口径）。
        if (!agent.IsEnabled)
        {
            await FailAsync(run, CliRunFailureReason.Unknown, "The agent is disabled.", cancellationToken);
            return null;
        }

        return new CliRunSetup(binding, runtime, provider, executablePath, agent, target, scope)
        {
            BindingProviderMatches = await BindingProviderMatchesAsync(binding, runtime, cancellationToken)
        };
    }

    /// <summary>取执行目标用的身份：一律取自运行记录与它的运行时。</summary>
    private static CliHostScope CreateScope(CliRun run, Entities.CliRuntime runtime, string providerKey) => new()
    {
        RunId = run.Id,
        AgentId = run.AgentId,
        TenantId = run.TenantId,
        UserId = run.CreatorId,
        ThreadId = run.ThreadId,
        RuntimeId = runtime.Id,
        ProviderKey = providerKey,
        TargetId = runtime.HostId,
        RuntimeMode = runtime.Mode
    };

    /// <summary>
    /// 绑定上按 CLI 解释的设置（模型、推理强度、自定义参数）能不能用在这次运行上：这次的 CLI 就是绑定的那家时才能。
    /// 使用方的落点可以把一轮放到另一家 CLI 上，那时把 claude 的参数交给 codex 只会让它起不来。
    /// </summary>
    private async Task<bool> BindingProviderMatchesAsync(CliAgentBinding binding, Entities.CliRuntime runtime, CancellationToken cancellationToken)
    {
        if (binding.CliRuntimeId == runtime.Id)
        {
            return true;
        }

        var bindingProvider = await _runtimeRepository.AsQueryable()
            .Where(r => r.Id == binding.CliRuntimeId)
            .Select(r => r.ProviderKey)
            .FirstOrDefaultAsync(cancellationToken);
        return string.Equals(bindingProvider, runtime.ProviderKey, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 这次运行的工作目录策略：使用方为这一轮指定了目录时按用户提供的目录对待（不写受管文件、永不删除、按目录记占用），否则按绑定。
    /// </summary>
    private static (CliWorkDirectoryMode Mode, string? UserWorkDirectory) ResolveWorkDirectoryPolicy(CliRun run, CliRunSetup setup)
        => string.IsNullOrWhiteSpace(run.AssignedWorkDirectory)
            ? (setup.Binding.WorkDirectoryMode, setup.Binding.UserWorkDirectory)
            : (CliWorkDirectoryMode.UserProvided, run.AssignedWorkDirectory);

    /// <summary>
    /// 使用方为这一轮指定的工作目录：规范化成目标上的绝对路径，核对它是目标声明的某个根或在其之下，不存在时按要求建出来。
    /// 不合规时这一轮以 <c>LaunchFailed</c> 结束（抛 <see cref="CliProviderConfigurationException"/>），一个文件都不写。
    /// </summary>
    /// <remarks>
    /// 规范化后的路径写回运行记录的内存副本：之后的布置、占用标记、续接与清理都按同一个写法认它（<c>..</c> 绕不出根）。
    /// 落在受管工作区根下的目录拒绝：回收器会把那里的子目录当成运行目录删掉，而按运行指定的目录是用户的，永不删除。
    /// </remarks>
    private async Task EnsureAssignedWorkDirectoryAsync(CliRun run, CliRunSetup setup, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(run.AssignedWorkDirectory))
        {
            return;
        }

        var target = setup.Target;
        var paths = target.FileSystem.Paths;
        string directory;
        try
        {
            directory = paths.GetFullPath(run.AssignedWorkDirectory);
        }
        catch (ArgumentException)
        {
            throw new CliProviderConfigurationException(setup.Provider.Key,
                $"The work directory '{run.AssignedWorkDirectory}' given for this run is not an absolute path on '{target.Name}'.");
        }

        if (paths.AreSame(directory, target.WorkspacesRoot) || paths.IsUnder(directory, target.WorkspacesRoot)
            || !target.WorkDirectoryRoots.Any(root => !string.IsNullOrWhiteSpace(root) && (paths.AreSame(directory, root) || paths.IsUnder(directory, root))))
        {
            throw new CliProviderConfigurationException(setup.Provider.Key,
                $"The work directory '{directory}' given for this run is outside the directories '{target.Name}' allows for runs.");
        }

        if (!await target.FileSystem.DirectoryExistsAsync(directory, cancellationToken) && run.CreateAssignedWorkDirectory)
        {
            await target.CreateWorkDirectoryAsync(new CliWorkDirectoryRequest(setup.Scope, directory), cancellationToken);
        }

        run.AssignedWorkDirectory = directory;
    }

    /// <summary>
    /// 第一次执行时定下续接指针：同一线程、同一 Agent 上最近一次结束的运行留下的会话
    /// （运行有派出者时只取同一派出者的）。<c>PerRun</c> 与没有线程的运行不续接。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ 在认领之后解析而不是在入队时：执行按线程串行（认领时判忙），指针要等前一轮真的结束才存在。
    /// 此前入队那一刻就定下，前一轮还在排队或执行时追加的下一轮拿到的是空指针（或者更早一轮的旧指针），
    /// 以全新的 CLI 会话运行，前一轮的上下文就此丢出续接链，而且因为根本没尝试续接，不会有「历史已丢失」的说明。
    /// </para>
    /// <para>
    /// 只在第一次执行时解析（<see cref="CliRun.StartedAt"/> 为空）：解析结果随进入 Running 一起落库，
    /// 停机打回队列或租约回收后的重新执行沿用它，包括续接被拒之后清空的那个空指针，
    /// 否则重新执行会再去续接一次刚被拒绝的会话。
    /// </para>
    /// <para>
    /// 只从同一派出者的运行取：线程归属入队时已核过，这里是第二道门：一条线程若曾被别人的运行写过会话指针，
    /// 那个指针也不该被接上。<c>PerRun</c> 每轮换目录，CLI 在新目录里找不到上一轮的会话存档，明知必被拒还发
    /// <c>--resume</c> 只会白跑一次重试并让用户为同一个问题付两次钱。
    /// </para>
    /// </remarks>
    private async Task ResolveResumePointerAsync(CliRun run, CliRunSetup setup, CancellationToken cancellationToken)
    {
        if (run.StartedAt is not null)
        {
            return;
        }

        string? pointer = null;
        string? resumeAt = null;
        var contextLost = false;

        if (ResolveWorkDirectoryPolicy(run, setup).Mode != CliWorkDirectoryMode.PerRun && run.ThreadId is { } threadId)
        {
            var runId = run.Id;
            var agentId = run.AgentId;
            var creatorId = run.CreatorId;

            // 跨租户查但不按租户比：线程与 Agent 都是带租户的行，它们的 Id 本身就把范围钉在运行自己的租户里，
            // 而多租户关闭时 TenantId 根本不映射，按它比较的查询翻译不了。
            // 被中断的轮次也取出来：它们从不被续接（会话停在半截），但它们排在被续接的那一轮之后时，那一段要截掉并向 agent 说明
            var finished = await _runRepository.AcrossTenants()
                .Where(r => r.ThreadId == threadId
                            && r.AgentId == agentId
                            && r.Id != runId
                            && (r.ProviderSessionId != null || r.FailureReason == CliRunFailureReason.Interrupted)
                            && (r.Status == CliRunStatus.Completed || r.Status == CliRunStatus.Failed
                                || r.Status == CliRunStatus.Cancelled || r.Status == CliRunStatus.TimedOut)
                            && (creatorId == null || r.CreatorId == creatorId))
                .OrderByDescending(r => r.CompletedAt)
                .ThenByDescending(r => r.CreationTime)
                .Select(r => new ResumeCandidate(
                    r.Id, r.ProviderSessionId, r.ProviderSessionEntryId, r.UserMessageId, r.CreationTime,
                    r.FailureReason == CliRunFailureReason.Interrupted))
                .ToListAsync(cancellationToken);

            var (kept, dropped, interrupted) = await SplitByRewindAsync(run, finished, cancellationToken);

            if (interrupted is not null)
            {
                // 上一轮停在了半截（进程没能接回来）：它在会话里留下的那一段截掉（截不了就开新会话），并告诉 agent 那一轮停在哪里，
                // 否则 agent 既不知道那一轮做到了哪一步，也可能把半截的工作当成做完了
                run.PerTurnContext = AppendInterruptionNotice(run.PerTurnContext, await DescribeInterruptedTurnAsync(interrupted.RunId));
                _logger.LogInformation(
                    "CLI run {RunId} follows run {InterruptedRunId}, which was interrupted; not resuming the interrupted session",
                    run.Id, interrupted.RunId);
            }

            if (kept is not null)
            {
                if (!dropped)
                {
                    pointer = kept.SessionId;
                }
                else if (setup.Provider.SupportsResumeAtEntry && !string.IsNullOrWhiteSpace(kept.SessionEntryId))
                {
                    // 线程回退过（编辑、重新生成）：从被保留的最后一轮处截断、分叉，被替换的轮次不进新会话
                    pointer = kept.SessionId;
                    resumeAt = kept.SessionEntryId;
                }
                else
                {
                    // 截断不了（provider 不支持，或那一轮是在记录截断点之前跑的）：开新会话，并在这一轮里说明之前的对话已不可用。
                    // 续接原会话会把被替换的轮次带回 agent 的记忆里，那正是编辑与重新生成要去掉的东西。
                    contextLost = true;
                    _logger.LogInformation(
                        "CLI run {RunId} cannot cut the session of thread {ThreadId} back to the kept turns; starting a fresh session",
                        run.Id, threadId);
                }
            }
        }

        run.ProviderSessionId = pointer;
        run.ResumeAtEntryId = resumeAt;
        run.ResumeExpected = pointer is not null;
        if (contextLost)
        {
            run.PerTurnContext = AppendContextLossNotice(run.PerTurnContext);
        }
    }

    /// <summary>
    /// 在同一线程上已经结束的运行里，找出这一轮该续接的那一轮，并判断它之后是否有被线程回退丢掉的轮次。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 该续接的是「回答的用户消息仍在线程里、且排在这一轮的用户消息之前」的最新一轮。编辑与重新生成会删掉线程里从某条用户消息起的全部消息：
    /// 回答被删消息的运行就不再是续接对象；它们比被保留的那一轮更新，说明会话里有要丢掉的轮次（<c>Dropped</c>）。
    /// 正常追问时，被保留的就是最新的那一轮，不需要截断。
    /// </para>
    /// <para>
    /// 被中断的轮次（<see cref="CliRunFailureReason.Interrupted"/>）从不被续接：它排在被保留的那一轮之后、又仍在线程里时，作为 <c>Interrupted</c> 返回，
    /// 它留在会话里的那一段同样算作要丢掉的（<c>Dropped</c>）。
    /// </para>
    /// <para>
    /// 没记下用户消息的运行（直接调调度器的调用方、本功能之前的运行）按创建时间算作「之前」，不当作被丢掉。
    /// 这一轮自己没记下用户消息时退回旧规则：续接最新的那一轮。
    /// </para>
    /// </remarks>
    private async Task<(ResumeCandidate? Kept, bool Dropped, ResumeCandidate? Interrupted)> SplitByRewindAsync(
        CliRun run, List<ResumeCandidate> finished, CancellationToken cancellationToken)
    {
        if (finished.Count == 0)
        {
            return (null, false, null);
        }

        if (run.UserMessageId is not { } currentMessageId)
        {
            return FirstKept(finished, _ => true);
        }

        var messageIds = finished.Where(c => c.UserMessageId.HasValue).Select(c => c.UserMessageId!.Value)
            .Append(currentMessageId)
            .Distinct()
            .ToList();

        Dictionary<Guid, int> orders;
        using (var scope = _scopeFactory.CreateScope())
        {
            var messages = scope.ServiceProvider.GetService<IRepository<AgentThreadMessage, Guid>>();
            if (messages is null)
            {
                return FirstKept(finished, _ => true);
            }

            // 只按主键查、不比租户列：消息 id 来自这条线程上的运行，本身就把范围钉住了
            orders = await messages.AsQueryable()
                .IgnoreQueryFilters()
                .Where(m => messageIds.Contains(m.Id))
                .Select(m => new { m.Id, m.Order })
                .ToDictionaryAsync(m => m.Id, m => m.Order, cancellationToken);
        }

        if (!orders.TryGetValue(currentMessageId, out var currentOrder))
        {
            // 这一轮要回答的消息已经不在了（排队期间被回退掉）：没有可据以判断的位置，退回旧规则
            return FirstKept(finished, _ => true);
        }

        return FirstKept(finished, candidate => candidate.UserMessageId is { } messageId
            ? orders.TryGetValue(messageId, out var order) && order < currentOrder
            : candidate.CreationTime < run.CreationTime);
    }

    /// <summary>
    /// 最新的、仍排在这一轮之前的那一轮（<paramref name="before"/>）就是续接点；在它之前碰到的被中断的轮次记下最新的那一个。
    /// 之前的每一轮都被丢掉了（编辑的是第一条消息）时没有续接点：开新会话就是对的。
    /// </summary>
    private static (ResumeCandidate? Kept, bool Dropped, ResumeCandidate? Interrupted) FirstKept(
        List<ResumeCandidate> finished, Func<ResumeCandidate, bool> before)
    {
        ResumeCandidate? interrupted = null;
        for (var i = 0; i < finished.Count; i++)
        {
            var candidate = finished[i];
            if (!before(candidate))
            {
                continue;
            }

            if (candidate.Interrupted)
            {
                interrupted ??= candidate;
                continue;
            }

            return (candidate, i > 0, interrupted);
        }

        return (null, false, interrupted);
    }

    /// <summary>被中断的那一轮的请求与回复最多带多少字符进说明。</summary>
    private const int InterruptedTurnExcerptLength = 2000;

    /// <summary>被中断的那一轮做到了哪一步：它的请求、停下的原因、调过的工具与写出的最后一段话。</summary>
    private async Task<InterruptedTurn> DescribeInterruptedTurnAsync(Guid runId)
    {
        using var scope = _scopeFactory.CreateScope();
        var row = await _runRepository.AcrossTenants()
            .Where(r => r.Id == runId)
            .Select(r => new { r.Prompt, r.Error })
            .FirstAsync(CancellationToken.None);

        var (reply, _, _) = await FoldReplyAsync(scope.ServiceProvider, runId);

        // 主 agent 自己调的工具（子 agent 的调用算在派出它的那一次里）
        var calls = reply.ToolCalls.Where(c => c.ParentId is null).ToList();
        return new InterruptedTurn(row.Prompt, row.Error, reply.Text, calls.Count, calls.LastOrDefault()?.Name);
    }

    /// <summary>
    /// 在本轮上下文尾部说明上一轮停在了半截：它要做什么、为什么停、停之前做了什么，以及工作目录里留着它的改动。
    /// </summary>
    private static string AppendInterruptionNotice(string? perTurnContext, InterruptedTurn turn)
    {
        var notice = new StringBuilder();
        notice.Append("NOTE: the previous turn was interrupted before it finished");
        notice.Append(string.IsNullOrWhiteSpace(turn.Reason) ? "." : $" ({turn.Reason.Trim()}).");
        notice.Append(" You are continuing in a new session, so that turn is not in your history. Its request was:")
            .AppendLine().AppendLine("<interrupted-request>")
            .AppendLine(Head(turn.Prompt, InterruptedTurnExcerptLength))
            .Append("</interrupted-request>").AppendLine();

        if (turn.ToolCalls > 0)
        {
            notice.Append($"Before it stopped it had made {turn.ToolCalls} tool call(s)");
            notice.Append(string.IsNullOrWhiteSpace(turn.LastTool) ? "." : $", the last one to `{turn.LastTool}`.");
            notice.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(turn.ReplyText))
        {
            notice.AppendLine("The last thing it had written was:")
                .AppendLine("<interrupted-reply>")
                .AppendLine(Tail(turn.ReplyText.Trim(), InterruptedTurnExcerptLength))
                .Append("</interrupted-reply>").AppendLine();
        }

        notice.Append("Any changes it made in the working directory are still there. Check the current state before continuing, ")
            .Append("and do not assume that turn's work was completed.");

        var text = notice.ToString();
        return string.IsNullOrWhiteSpace(perTurnContext)
            ? text
            : perTurnContext + Environment.NewLine + Environment.NewLine + text;
    }

    private static string Head(string text, int maxLength)
        => text.Length <= maxLength ? text : text.FitToLength(maxLength) + " …";

    private static string Tail(string text, int maxLength)
        => text.Length <= maxLength ? text : "… " + text[^maxLength..];

    /// <summary>
    /// 进入 Running：只写这几列，并且以「仍是派给本次执行的、没有人要求取消」为条件。返回 false 表示不该再往下跑。
    /// </summary>
    /// <remarks>
    /// 此前是整实体回写：运行在认领时加载，准备阶段（预算、技能、工作区）期间别的副本写下的事实
    /// 全被加载时的旧值盖掉。取消请求落在另一个副本上（写 <c>CancelRequested=true</c>）时被改回 false，续租再也看不见它，
    /// 运行照常跑完并计费，而取消端点答过 200；准备阶段比租约长时，续租写下的新租约被改回认领时已过期的旧值，
    /// 回收器把正在跑的运行打回队列、另一个宿主再认领一次。
    /// 条件不成立有两种原因：有人要求取消（落取消终态），或者这一行已经不归这次执行（租约被回收后别处接手），后者不再碰它。
    /// </remarks>
    private async Task<bool> MarkRunningAsync(CliRun run, CliWorkspace workspace, CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;
        var workDirectory = workspace.WorkDirectory;
        var messageCount = await ResolveLastSequenceAsync(run, cancellationToken);
        var providerSessionId = run.ProviderSessionId;
        var resumeAtEntryId = run.ResumeAtEntryId;
        var resumeExpected = run.ResumeExpected;
        var perTurnContext = run.PerTurnContext;

        // 续接指针与开始时间一起写：开始时间非空 = 指针已经定下，重新执行沿用它（见 ResolveResumePointerAsync）。
        var started = await _runRepository.AcrossTenants()
            .Where(r => r.Id == run.Id && r.Status == CliRunStatus.Dispatched && !r.CancelRequested)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, CliRunStatus.Running)
                .SetProperty(r => r.StartedAt, startedAt)
                .SetProperty(r => r.WorkDirectory, workDirectory)
                .SetProperty(r => r.MessageCount, messageCount)
                .SetProperty(r => r.ProviderSessionId, providerSessionId)
                .SetProperty(r => r.ResumeAtEntryId, resumeAtEntryId)
                .SetProperty(r => r.ResumeExpected, resumeExpected)
                .SetProperty(r => r.PerTurnContext, perTurnContext), cancellationToken);

        if (started == 0)
        {
            if (await IsCancelRequestedAsync(run.Id))
            {
                await MarkCancelledAsync(run);
            }
            else
            {
                _logger.LogWarning(
                    "CLI run {RunId} is no longer dispatched to this execution; leaving it to its current owner", run.Id);
            }

            return false;
        }

        // 让内存里的实体与库里一致：之后的进程启动（事件序号的起点）与收尾读的是这里的值。
        run.Status = CliRunStatus.Running;
        run.StartedAt = startedAt;
        run.WorkDirectory = workDirectory;
        run.MessageCount = messageCount;
        return true;
    }

    /// <summary>
    /// 本次执行的事件从哪个序号之后接着编：取运行记录上的计数与已落库事件的最大序号中较大的那个。
    /// </summary>
    /// <remarks>
    /// <c>MessageCount</c> 只在终态与续接重试前写，宿主停机打回队列、宿主崩溃后的租约回收都不动它，
    /// 而那一次已经落下的事件还在表里（它们是被打断那一次的真实记录，SSE 按序号补发，不删）。
    /// 只信计数，重新执行的第一条事件就撞上 (RunId, Sequence) 唯一索引，运行以 Failed 结束，
    /// 两条恢复路径在「第一次至少落过一条事件」的常态下从来不会成功，而 CLI 已经又被拉起并计费了一次。
    /// 摘掉过滤器按运行查：这里切在运行自己的租户里，但序号的唯一性不分租户。
    /// </remarks>
    private async Task<int> ResolveLastSequenceAsync(CliRun run, CancellationToken cancellationToken)
    {
        var persisted = await _messageRepository.AsQueryable()
            .IgnoreQueryFilters()
            .Where(m => m.RunId == run.Id)
            .MaxAsync(m => (int?)m.Sequence, cancellationToken);

        return Math.Max(run.MessageCount, persisted ?? 0);
    }

    private async Task<CliWorkspace> PrepareWorkspaceAsync(
        CliRun run, CliRunSetup setup, CliAgentOptions options, CancellationToken cancellationToken)
    {
        await EnsureAssignedWorkDirectoryAsync(run, setup, cancellationToken);
        var (workDirectoryMode, userWorkDirectory) = ResolveWorkDirectoryPolicy(run, setup);

        var skills = setup.Binding.MaterializeSkills
            ? await LoadSkillsAsync(run.AgentId, cancellationToken)
            : [];

        // 凭据必须在布置工作区之前签发：MCP 配置文件是工作区的一部分，由布置器写出。
        var writeBackToken = options.WriteBack.Enabled
            ? await _tokenService.IssueAsync(run, options.WriteBack.TokenLifetime, cancellationToken)
            : null;
        _writeBackToken = writeBackToken;

        var context = new CliRunContext
        {
            RunId = run.Id,
            AgentId = run.AgentId,
            TenantId = run.TenantId,
            ThreadId = run.ThreadId,
            UserId = run.CreatorId,
            Provider = setup.Provider,
            StableBrief = setup.Binding.InjectAgentInstructions
                ? _briefComposer.Compose(setup.Agent, setup.Provider)
                : string.Empty,
            PerTurnContext = run.PerTurnContext,
            WorkDirectoryMode = workDirectoryMode,
            UserWorkDirectory = userWorkDirectory,
            Skills = skills,
            McpConfigJson = _mcpConfigComposer.Compose(
                setup.Binding.McpConfigJson, writeBackToken, options.WriteBack, setup.Target)
        };

        try
        {
            return await PrepareOrReuseAsync(setup.Target, run, context, cancellationToken);
        }
        catch (CliWorkspaceClaimedException claimed)
        {
            if (!await HasFinishedAsync(claimed.OwnerRunId))
            {
                throw;
            }

            // 占用者已经结束，标记是它硬崩溃时留下的（清理没发生）。用户目录模式下那个目录是同一 Agent 所有线程共用的，
            // 不回收的话别的线程的每一次运行都会被拒，直到有人手工删掉这个文件。
            _logger.LogWarning(
                "Workspace {Directory} was left claimed by CLI run {OwnerRunId}, which has finished; reclaiming it for run {RunId}",
                claimed.Directory, claimed.OwnerRunId, run.Id);
            return await PrepareOrReuseAsync(
                setup.Target, run, context with { ReclaimableClaimRunId = claimed.OwnerRunId }, cancellationToken);
        }
    }

    private async Task<CliWorkspace> PrepareOrReuseAsync(
        ICliHostTarget target, CliRun run, CliRunContext context, CancellationToken cancellationToken)
    {
        // 续接一个还在原地的工作目录，比重新布置更接近 agent 上一轮结束时的状态
        //（它可能已经 clone 了仓库、装了依赖）。
        if (!string.IsNullOrWhiteSpace(run.WorkDirectory))
        {
            var reused = await _workspacePreparer.ReuseAsync(target, run.WorkDirectory, context, cancellationToken);
            if (reused is not null)
            {
                return reused;
            }
        }

        return await _workspacePreparer.PrepareAsync(target, context, cancellationToken);
    }

    /// <summary>
    /// 某次运行是否已经到达终态。查不到的运行不算结束：失败关闭，标记归属一个本库不认识的运行时照样拒绝。
    /// </summary>
    private async Task<bool> HasFinishedAsync(Guid runId)
    {
        var status = await _runRepository.AcrossTenants()
            .Where(r => r.Id == runId)
            .Select(r => (CliRunStatus?)r.Status)
            .FirstOrDefaultAsync(CancellationToken.None);

        return status is { } finished && IsTerminal(finished);
    }

    private async Task<List<CliSkillPayload>> LoadSkillsAsync(Guid agentId, CancellationToken cancellationToken)
    {
        var grants = await _grantService.GetGrantsAsync(agentId, cancellationToken);
        if (grants.SkillSlugs is not { Count: > 0 })
        {
            return [];
        }

        var payloads = new List<CliSkillPayload>(grants.SkillSlugs.Count);
        foreach (var slug in grants.SkillSlugs)
        {
            var skill = await _skillService.GetBySlugAsync(slug);
            if (!skill.Succeeded || skill.Data is null)
            {
                // 一个取不到的技能不该让整次运行失败：agent 少一份指南仍能干活，
                // 而失败会让用户看不出「只是某个技能没配好」。
                _logger.LogWarning("Skipping skill '{Slug}' for agent {AgentId}: {Message}", slug, agentId, skill.Message);
                continue;
            }

            payloads.Add(new CliSkillPayload
            {
                Slug = skill.Data.Slug,
                Description = string.IsNullOrWhiteSpace(skill.Data.WhenToUse) ? skill.Data.Description : skill.Data.WhenToUse,
                Content = skill.Data.Content
            });
        }

        return payloads;
    }

    /// <summary>
    /// 跑一次进程，返回这一次的结果、最后看到的会话 id 与事件序号。不写终态：落不落、落哪一次由调用方决定。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>continuation</c>：接着上一次执行的会话往下跑时发给 agent 的那句话，代替这一轮的提示、本轮上下文与附件（它们已经在会话里）；
    /// null 时照常发这一轮的提示。
    /// </para>
    /// <para>
    /// <c>reattached</c>：服务端重启前起的、接回来的进程。不起新进程：适配器从它的第一行输出重放（与停机前看到的是同一份输入，
    /// 状态因此与停机前一样），已经落库的事件按序号跳过、不写第二遍，进程早已收到过的 stdin 行不再发（见 <see cref="CliReplayProcess"/>）。
    /// </para>
    /// <para>
    /// <c>live</c>：同线程上一条运行交出的、还在等后台任务的进程（§6.18）。不起新进程：这一轮的用户帧写进它还开着的 stdin，接着读同一条事件流。
    /// 这一轮要的启动方式与那个进程起来时不同（换了模型、权限档、工具、环境），或者它的最后一轮恰好收尾了，返回
    /// <see cref="CliAttempt.ContinuationRefused"/>（进程已结束），调用方另起进程续接同一会话。
    /// </para>
    /// <para>
    /// 一轮结束而 CLI 还有后台任务时，这次运行在 <see cref="CliRunHandoffs"/> 登记为等着的运行；同线程的下一条运行来取时，在两轮之间停下读取，
    /// 进程不终止，结果里带着要交出去的进程（<see cref="CliAttempt.Handoff"/>），由 <see cref="ExecuteAsync"/> 在落终态之后交出。
    /// </para>
    /// <para>
    /// 宿主停机时，交出了重新附着句柄的进程不终止：抛 <see cref="CliRunDetachedException"/>，交给 <see cref="ExecuteAsync"/> 让它留在宿主上接着跑。
    /// 接过来的进程不在此列（句柄记在起它的那条运行上），停机时照常终止。
    /// </para>
    /// </remarks>
    private async Task<CliAttempt> RunProcessAsync(
        CliRun run, CliRunSetup setup, CliWorkspace workspace, CliAgentOptions options, UsageHold? usage,
        CancellationToken cancellationToken, string? continuation = null, ICliReattachedProcess? reattached = null,
        CliLiveSession? live = null)
    {
        var adapter = live?.Adapter ?? _adapterFactory.Create(setup.Provider.Protocol);

        // 档案接回来的进程也要：适配器按它的权限档位应答接回来之后的权限请求，按缺省（全权）答就把一条只读的运行放开了。
        // 接回来的进程早就起好了：启动环境（登录态探测会在目标上起一条命令）与附件只是给新进程用的
        var profile = _launchProfile ??= await ResolveLaunchProfileAsync(run, setup, workspace, CliLaunchProfilePurpose.Launch, cancellationToken);
        var launchEnvironment = reattached is null
            ? await _launchEnvironmentComposer.ComposeAsync(
                setup.Provider, setup.ExecutablePath, workspace, setup.Target, options, WithInteractionTools(profile, setup.Provider, run), cancellationToken)
            : CliLaunchEnvironment.None;
        // 图片与 PDF 随提示投递；其余上传是文件，agent 用工具打开：名字、大小与位置写进这一轮的上下文
        var (attachments, files) = continuation is null && reattached is null
            ? await LoadAttachmentsAsync(run.Id, profile, cancellationToken)
            : ([], []);
        var launchContext = BuildLaunchContext(run, setup, workspace, launchEnvironment, profile, options, continuation);
        launchContext = launchContext with
        {
            Attachments = setup.Provider.AcceptsAttachments ? attachments : [],
            Files = files,
            TurnContext = CliAttachedFiles.AppendTo(launchContext.TurnContext, files)
        };
        RememberSession(run.ProviderSessionId);
        var spec = adapter.BuildProcess(launchContext);

        // 接过来的进程是按上一轮的启动方式起的：这一轮要的不一样，就不能接着用它（模型、权限档与工具都钉在启动参数里）
        if (live is not null
            && CliLaunchSpecs.FindDifference(live.Spec, live.RunScopedVariables, spec, launchEnvironment.RunScopedVariables,
                setup.Provider.ChangesEffortInSession ? InSessionOptions : null) is { } difference)
        {
            _logger.LogInformation(
                "CLI run {RunId} needs a different launch than the process it was handed ({Difference}); " +
                "ending that process and starting a new one on the same session", run.Id, difference);
            return await RefuseContinuationAsync(run, live, options.ToolResults,
                "The process kept for the background tasks was launched with different settings (model, reasoning effort, permission mode or tools) than this turn needs, so it was ended with them.");
        }

        var idle = ResolveIdleWatchdog(setup.Binding, options);
        using var watchdog = new CliRunWatchdog(idle, options.ToolWatchdog, ResolveHardTimeout(run, options, reattached is not null), cancellationToken);

        // 已经落库的最后一个事件序号：重放产出的事件到这里为止都写过了。新进程从计数起编，没有要跳过的
        var persistedThrough = reattached is null ? int.MinValue : await ResolveLastSequenceAsync(run, CancellationToken.None);

        var stopwatch = Stopwatch.StartNew();
        var replay = reattached is null ? null : new CliReplayProcess(reattached, options.Reattach.ReplayOrderingWait, _logger);
        var process = live?.Process ?? replay ?? await setup.Target.StartAsync(spec, cancellationToken);
        if (live is not null)
        {
            live.Adopted = true;
        }

        // 事件流的读取令牌属于进程：这次运行的看门狗与取消接到它上面；进程交出去之后这次运行的令牌再被取消，不碰它
        var lifetime = live?.Lifetime ?? new CancellationTokenSource();
        CliLiveSession? handoff = null;
        var released = false;
        try
        {
            if (reattached is null && live is null && launchEnvironment.UsageMeteredElsewhere != run.UsageMeteredElsewhere)
            {
                await RecordMeteringAsync(run, launchEnvironment.UsageMeteredElsewhere);
            }

            if (reattached is null && live is null && process.ReattachHandle is { } handle)
            {
                await RecordReattachHandleAsync(run, handle);
            }

            var continuable = adapter as ICliTurnContinuation;
            if (live is not null)
            {
                // 提示写进去之前：此刻拥有进程的是这条运行（链接 + 事件），进程的第一个模型调用就该认它
                await TakeOverProcessAsync(run, live);
            }

            if (live is not null && !await continuable!.ContinueAsync(launchContext, cancellationToken))
            {
                await ReleaseProcessLinkAsync(run);
                // 交接与 CLI 最后一轮的收尾撞在了一起：这个进程已经不再读，这一轮另起进程续接同一会话
                _logger.LogInformation("The process handed to CLI run {RunId} ended its last turn meanwhile; starting a new one on the same session", run.Id);
                released = true;
                return await RefuseContinuationAsync(run, live, options.ToolResults, null);
            }

            using var sequence = new CliEventSequence(run.MessageCount);
            var sessionId = live?.SessionId ?? run.ProviderSessionId;
            var externallyCancelled = false;
            var detach = false;
            var toolNames = new Dictionary<string, string>(StringComparer.Ordinal);
            var toolCalls = 0;

            // 有人值守：权限请求（「先问我」与「计划」档）、agent 的问题与计划交给使用方，回答由批准通道在后台轮询、写回 CLI（见 CliRunApprovals）
            var approvals = launchContext.AsksForApproval || launchContext.RelaysInteractions
                ? new CliRunApprovals(run, adapter, sequence, watchdog, _scopeFactory, _signalHub, options.Approval, persistedThrough,
                    (evt, slot) => PersistAndSignalAsync(run, evt, slot, options.ToolResults), _logger)
                : null;
            using var approvalsStop = CancellationTokenSource.CreateLinkedTokenSource(watchdog.Token);
            var approvalsLoop = approvals?.RunAsync(approvalsStop.Token) ?? Task.CompletedTask;

            // 停下单个任务（子 agent、后台命令）：请求由任一副本落库，这里轮询到后写给 CLI（见 CliRunTaskStops）。协议做不到时没有这条通道
            var taskStopsLoop = adapter is ICliTaskControl taskControl
                ? new CliRunTaskStops(run.Id, taskControl, _scopeFactory, _signalHub, options.Approval.PollInterval, _logger).RunAsync(approvalsStop.Token)
                : Task.CompletedTask;

            // 线程消息队列交给这一轮的消息：执行这条运行的副本轮询到后写进进程，agent 在两步之间收下（见 CliRunQueuedInput）。
            // 协议或 provider 做不到、没有线程时不交，消息等这一轮结束。
            // 可重新附着的进程（本次起的、或接回来的）记下每条写在哪个事件之后，重放时原样再交一次，stdin 行数与适配器的状态都对得上
            var takesMessages = adapter is ICliTurnInput && setup.Provider.TakesMessagesMidTurn && run.ThreadId is not null;
            if (run.TakesMessagesMidTurn != takesMessages)
            {
                await RecordTakesMessagesAsync(run, takesMessages);
            }

            var queuedInput = takesMessages
                ? new CliRunQueuedInput(run.Id, run.ThreadId!.Value, (ICliTurnInput)adapter, continuable, setup.Provider.AcceptsAttachments,
                    _scopeFactory, options.Approval.PollInterval, _logger,
                    live is null && process.ReattachHandle is not null
                        ? new CliQueuedInputRecord(
                            sequence, reattached is null ? null : run.HandedInputs, persistedThrough, () => replay?.CaughtUp ?? true)
                        : null)
                : null;
            var queuedInputLoop = queuedInput?.RunAsync(approvalsStop.Token) ?? Task.CompletedTask;

            using var ticker = new Timer(_ => watchdog.CheckAndTrip(), null, WatchdogTick, WatchdogTick);

            // 等后台任务时能不能把进程交给同线程的下一条运行：协议做得到、有线程、工作目录跨轮保留（PerRun 每轮换目录、从不续接）
            var mayHandOff = _handoffs is not null && continuable is not null && run.ThreadId is not null
                             && ResolveWorkDirectoryPolicy(run, setup).Mode != CliWorkDirectoryMode.PerRun;

            var events = live?.Events ?? adapter.RunAsync(process.Transport, launchContext, lifetime.Token).GetAsyncEnumerator(lifetime.Token);
            var next = live?.PendingNext;
            try
            {
                using var forward = watchdog.Token.Register(static state => ((CancellationTokenSource)state!).Cancel(), lifetime);
                while (true)
                {
                    next ??= events.MoveNextAsync().AsTask();

                    // 在等后台任务、同线程的下一条运行来取进程了：两轮之间（没有一轮在进行）就停下读取，把进程连同正在等的这次读取交出去
                    if (_waiting is { } waiting && !next.IsCompleted)
                    {
                        if (!waiting.Requested.Task.IsCompleted)
                        {
                            await Task.WhenAny(next, waiting.Requested.Task);
                        }

                        if (!next.IsCompleted && waiting.Requested.Task.IsCompleted)
                        {
                            if (waiting.Abandoned)
                            {
                                // 来取的那条放弃了（被停下）：重新登记，等下一个
                                _handoffs!.Hand(waiting, null);
                                _waiting = _handoffs.Offer(run.Id, waiting.ThreadId, run.AgentId, run.CliRuntimeId);
                                continue;
                            }

                            if (continuable!.IsAwaitingFollowUp)
                            {
                                handoff = new CliLiveSession
                                {
                                    Process = process,
                                    Adapter = adapter,
                                    Events = events,
                                    PendingNext = next,
                                    Lifetime = lifetime,
                                    Spec = live?.Spec ?? spec,
                                    RunScopedVariables = live?.RunScopedVariables ?? launchEnvironment.RunScopedVariables,
                                    Workspace = workspace,
                                    SessionId = sessionId,
                                    FromRunId = run.Id,
                                    LaunchedByRunId = live?.LaunchedByRunId ?? run.ProcessLaunchedByRunId ?? run.Id,
                                    WriteBackTokenHash = run.WriteBackTokenHash,
                                    WriteBackTokenExpiresAt = run.WriteBackTokenExpiresAt,
                                    WriteBackToken = live is not null ? live.WriteBackToken : _writeBackToken
                                };
                                break;
                            }

                            // 任务通知开出的一轮正在进行：等它结束（下一个 TurnCompleted）再交，或者它就是最后一轮，进程随它结束
                        }
                    }

                    if (!await next)
                    {
                        break;
                    }

                    next = null;
                    var evt = events.Current;
                    watchdog.Observe(evt);

                    if (!string.IsNullOrWhiteSpace(evt.SessionId))
                    {
                        sessionId = evt.SessionId;
                    }

                    if (evt.Type == CliAgentEventType.ToolUse)
                    {
                        toolCalls++;
                    }

                    // 序号与停机前那一次逐个对得上（同一份输入、同一个起点），到已落库的为止不再写。
                    // 批准通道写结局也从同一个序号里取号，所以取号与落库都在闸门里
                    await sequence.Gate.WaitAsync(CancellationToken.None);
                    try
                    {
                        var slot = ++sequence.Current;
                        var named = NameToolResult(evt, toolNames);
                        if (approvals is not null && named.Type == CliAgentEventType.PermissionRequest)
                        {
                            named = await approvals.OpenAsync(named, slot);
                        }

                        // 一轮在运行中途结束（CLI 还有后台任务、之后自己开下一轮）：这一轮的回答现在就写进线程，
                        // 事件带上那条消息的 id。重放到已落库的那条时消息早已写过
                        if (named.Type == CliAgentEventType.TurnCompleted && slot > persistedThrough)
                        {
                            named = await WriteTurnReplyAsync(run, named);
                        }

                        // 队列交给这一轮的一条消息被收下：收下之前的那段回答写成一条消息，这条用户消息写在它后面，之后的回答另起一条
                        if (named.Type == CliAgentEventType.UserMessage && slot > persistedThrough)
                        {
                            named = await RecordUserMessageAsync(run, named);
                        }

                        if (slot > persistedThrough)
                        {
                            await PersistEventAsync(run, named, slot, options.ToolResults, cancellationToken);
                            _signalHub.Signal(run.Id);
                            await TrackFollowUpAsync(run, named);
                        }

                        await ReplayRecordedWritesAsync(sequence, queuedInput, approvals);
                    }
                    finally
                    {
                        sequence.Gate.Release();
                    }

                    // 这一轮的回答已经在线程里，CLI 在等后台任务：同线程的下一条消息不必排在等待后面，可以来取这个进程
                    if (mayHandOff && _waiting is null && evt.Type == CliAgentEventType.TurnCompleted && continuable!.IsAwaitingFollowUp)
                    {
                        _waiting = _handoffs!.Offer(run.Id, run.ThreadId!.Value, run.AgentId, run.CliRuntimeId);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 三种来源：用户取消、宿主停机、看门狗。用 watchdog.Failure 区分 ——
                // 它是唯一在做出判断的那一刻记下原因的地方。
                externallyCancelled = watchdog.Failure is null;

                // 宿主停机（不是用户要求取消）而进程接得回来：不终止它，让它在执行宿主上接着跑。
                // 接过来的进程的句柄记在起它的那条运行上，这条运行接不回它，照常终止
                detach = externallyCancelled && live is null && process.ReattachHandle is not null && !await IsCancelRequestedAsync(run.Id);
            }
            finally
            {
                stopwatch.Stop();
                await approvalsStop.CancelAsync();
                await approvalsLoop;
                await taskStopsLoop;
                await queuedInputLoop;
                RememberSession(sessionId);
                if (handoff is null)
                {
                    await DisposeEventsAsync(events, run.Id);
                }

                if (!detach && handoff is null)
                {
                    var stillRunning = !process.HasExited;
                    await process.TerminateAsync(CancellationToken.None);

                    // 会话已经结束（不是取消、不是看门狗），进程却没有自己干净退出、要靠终止：通常是它起的子进程（dev server 之类）握着它。
                    // 退出码不改变已经交付的回答（适配器按协议判结局），这里只留下这件事
                    if (stillRunning && !externallyCancelled && watchdog.Failure is null && process.ExitCode is not 0)
                    {
                        _logger.LogInformation(
                            "CLI run {RunId}: the agent process did not exit by itself after its session ended and was terminated (exit code {ExitCode})",
                            run.Id, process.ExitCode);
                    }
                }

                // 进程已经不在：还没写给 CLI 的权限请求随它结束（按拒绝）。进程留在宿主上等重新附着时它们保持挂起，接回来照常等回答
                if (!detach && approvals is not null)
                {
                    await CloseApprovalsAsync(run, approvals);
                }
            }

            if (detach)
            {
                throw new CliRunDetachedException();
            }

            // 这个进程已经结束：句柄作废，之后（重试的新进程、或者停机）不能再拿它去接
            if (handoff is null && process.ReattachHandle is not null)
            {
                await ClearReattachHandleAsync(run);
            }

            if (externallyCancelled)
            {
                // 外部取消还要再分一次：用户取消在这里落终态；宿主停机则把决定交回
                // ExecuteAsync 的停机分支（打回队列）—— 进程已经杀掉，这条运行要在别的副本上重跑。
                if (!await IsCancelRequestedAsync(run.Id))
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }

            var outcome = new CliSessionOutcome
            {
                // 交出去的进程还在跑：没有退出码
                ExitCode = handoff is null ? process.ExitCode : null,
                StderrTail = process.Transport.StderrTail,
                // 接回来的进程在停机前就开始跑了：时长从运行开始算，而不是从接回来那一刻
                Elapsed = reattached is not null && run.StartedAt is { } startedAt ? DateTime.UtcNow - startedAt : stopwatch.Elapsed,
                Cancelled = externallyCancelled,
                WatchdogFailure = watchdog.Failure
            };

            // 交出进程时只结算这次运行的这一段：适配器随后从零算下一条运行的
            var reported = handoff is null ? adapter.GetResult(outcome) : continuable!.CompleteRunSegment(outcome);
            await RecordCapabilitiesAsync(run.CliRuntimeId, reported.Capabilities);
            var turn = await ResolveTurnUsageAsync(run, setup, reported);

            // 从这里起结果里的用量是这一轮的：运行记录、用量日志、配额都只认它
            var result = reported with { Usage = turn.Usage, UsageIsSessionTotal = false, TurnUsage = null };
            if (!run.UsageMeteredElsewhere)
            {
                usage?.Record(SumCost(ResolveModelCosts(setup, result)));
            }

            return new CliAttempt(result, sessionId, sequence.Current, turn.SessionTotals, toolCalls) { Handoff = handoff };
        }
        finally
        {
            // 交出去的进程归接手的运行；不接着用而已经结束掉的不再释放一次
            if (handoff is null && !released)
            {
                await process.DisposeAsync();
                lifetime.Dispose();
            }
        }
    }

    /// <summary>结束读取：适配器的收尾（关 stdin）在这里发生。读取本身已经因为取消或进程结束而失败时，收尾的异常不盖掉原来的结局。</summary>
    private async Task DisposeEventsAsync(IAsyncEnumerator<CliAgentEvent> events, Guid runId)
    {
        try
        {
            await events.DisposeAsync();
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "Closing the event stream of CLI run {RunId} failed after the process ended", runId);
        }
    }

    /// <summary>
    /// 接过来的进程接不了这一轮：结束它（后台任务随之结束），说明的话（有的话）落成这一轮的一条 <c>background_tasks_stopped</c> 状态，
    /// 返回 <see cref="CliAttempt.ContinuationRefused"/>，调用方另起进程续接同一会话。
    /// </summary>
    private async Task<CliAttempt> RefuseContinuationAsync(CliRun run, CliLiveSession live, CliToolResultOptions limits, string? notice)
    {
        live.Adopted = true;
        await live.EndAsync();

        if (notice is not null)
        {
            run.MessageCount++;
            await PersistEventAsync(run, new CliAgentEvent
            {
                Type = CliAgentEventType.Status,
                Status = "background_tasks_stopped",
                Content = notice,
                SessionId = live.SessionId
            }, run.MessageCount, limits, CancellationToken.None);
            _signalHub.Signal(run.Id);
        }

        return new CliAttempt(new CliAgentResult { Status = CliRunStatus.Failed }, live.SessionId, run.MessageCount, null)
        {
            ContinuationRefused = true
        };
    }

    /// <summary>CLI 在这次握手里报的能力记在运行时上，整份覆盖；记不下来只影响展示，不影响这一轮。</summary>
    private async Task RecordCapabilitiesAsync(Guid runtimeId, CliAgentCapabilities? capabilities)
    {
        if (capabilities is null)
        {
            return;
        }

        try
        {
            var json = JsonSerializer.Serialize(capabilities, TnziJsonDefaults.Options);
            var now = DateTime.UtcNow;
            await _runtimeRepository.AsQueryable()
                .Where(r => r.Id == runtimeId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.CapabilitiesJson, json)
                    .SetProperty(r => r.CapabilitiesReportedAt, now), CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not record the capabilities runtime {RuntimeId} reported", runtimeId);
        }
    }

    /// <summary>候选基线最多看几轮：编辑 / 重新生成丢掉的轮次之外，再往前的不会是 CLI 恢复出来的那份。</summary>
    private const int UsageBaselineCandidates = 5;

    /// <summary>
    /// 把这一次进程报出的用量折算成这一轮的（见 <see cref="CliTurnUsage"/>），并定下这一轮结束时的会话累计值。
    /// </summary>
    /// <remarks>
    /// 续接指针此时还是启动这次进程时的那个（终态才改写 <see cref="CliRun.ProviderSessionId"/>）；续接被拒后的重试启动前已清空，
    /// 那一次按没有续接算。没有 result 帧的 stream-json 运行存一份估算的累计值，供下一轮在它与更早那份之间选。
    /// </remarks>
    private async Task<CliTurnUsage.Resolution> ResolveTurnUsageAsync(CliRun run, CliRunSetup setup, CliAgentResult reported)
    {
        var resumed = !string.IsNullOrWhiteSpace(run.ProviderSessionId);
        var candidates = resumed ? await LoadUsageBaselinesAsync(run) : [];
        var turn = CliTurnUsage.Resolve(reported, resumed, candidates);

        if (turn.SessionTotals is null && setup.Provider.Protocol == CliAgentProtocol.StreamJson)
        {
            var estimate = CliTurnUsage.EstimateTotals(candidates.FirstOrDefault()?.Totals, turn.Usage);
            turn = turn with { SessionTotals = estimate.Count > 0 ? estimate : null };
        }

        if (turn.BaselineRunId is { } baselineRunId)
        {
            _logger.LogDebug(
                "CLI run {RunId}: session totals carried over from run {BaselineRunId}; recording this turn's share only",
                run.Id, baselineRunId);
        }
        else if (turn.Ambiguous)
        {
            // 续接了却认不出恢复的是哪份：按「没恢复」记，若 CLI 其实恢复了一份没记下的总量，这一轮会记多
            _logger.LogWarning(
                "CLI run {RunId} resumed session {SessionId}, but none of the recorded session totals fits the reported totals; " +
                "recording the reported totals as this turn's usage",
                run.Id, run.ProviderSessionId);
        }

        return turn;
    }

    /// <summary>
    /// 候选基线：同一线程、同一 Agent（有派出者时同一派出者）上，结束在这次要续接的会话上的那些轮次记下的会话累计值，最近结束的在前。
    /// </summary>
    /// <remarks>
    /// 没有 <see cref="CliRun.SessionUsageJson"/> 的旧记录用它的 <see cref="CliRun.UsageJson"/>：这个字段出现之前，用量列存的就是 CLI 报的累计值。
    /// 认不认它交给 <see cref="CliTurnUsage.Resolve"/> 的覆盖校验。跨租户查而不比租户列，理由同 <see cref="ResolveResumePointerAsync"/>。
    /// </remarks>
    private async Task<List<CliTurnUsage.Baseline>> LoadUsageBaselinesAsync(CliRun run)
    {
        if (run.ThreadId is not { } threadId || string.IsNullOrWhiteSpace(run.ProviderSessionId))
        {
            return [];
        }

        var runId = run.Id;
        var agentId = run.AgentId;
        var creatorId = run.CreatorId;
        var sessionId = run.ProviderSessionId;

        var rows = await _runRepository.AcrossTenants()
            .Where(r => r.ThreadId == threadId
                        && r.AgentId == agentId
                        && r.Id != runId
                        && r.ProviderSessionId == sessionId
                        && (r.SessionUsageJson != null || r.UsageJson != null)
                        && (r.Status == CliRunStatus.Completed || r.Status == CliRunStatus.Failed
                            || r.Status == CliRunStatus.Cancelled || r.Status == CliRunStatus.TimedOut)
                        && (creatorId == null || r.CreatorId == creatorId))
            .OrderByDescending(r => r.CompletedAt)
            .ThenByDescending(r => r.CreationTime)
            .Select(r => new { r.Id, Json = r.SessionUsageJson ?? r.UsageJson })
            .Take(UsageBaselineCandidates)
            .ToListAsync(CancellationToken.None);

        var candidates = new List<CliTurnUsage.Baseline>(rows.Count);
        foreach (var row in rows)
        {
            if (ReadUsage(row.Json) is { Count: > 0 } totals)
            {
                candidates.Add(new CliTurnUsage.Baseline(row.Id, totals));
            }
        }

        return candidates;
    }

    private static Dictionary<string, CliAgentTokenUsage>? ReadUsage(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, CliAgentTokenUsage>>(json, TnziJsonDefaults.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 续接被拒后、开新会话重试之前：只记下非终态的事实，运行保持 Running。
    /// </summary>
    /// <remarks>
    /// 条件更新（仍是 Running 才写）而不是整实体回写：整实体会把加载时的 <c>CancelRequested=false</c>
    /// 盖到取消端点刚写下的事实上。事件序号要落库：重试被停机打断、运行回到队列时，下一次执行从这里接着编号；
    /// 失效的会话指针也要清掉，否则下一次执行还会拿它去续接。
    /// </remarks>
    private async Task PrepareFreshSessionRetryAsync(CliRun run, CliAttempt rejected)
    {
        run.MessageCount = rejected.Sequence;
        run.ProviderSessionId = null;
        run.ResumeAtEntryId = null;
        run.ResumeExpected = false;
        run.PerTurnContext = AppendContextLossNotice(run.PerTurnContext);

        var perTurnContext = run.PerTurnContext;
        await _runRepository.AcrossTenants()
            .Where(r => r.Id == run.Id && r.Status == CliRunStatus.Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.MessageCount, rejected.Sequence)
                .SetProperty(r => r.ProviderSessionId, (string?)null)
                .SetProperty(r => r.ResumeAtEntryId, (string?)null)
                .SetProperty(r => r.ResumeExpected, false)
                .SetProperty(r => r.PerTurnContext, perTurnContext), CancellationToken.None);
    }

    /// <summary>
    /// 把没走完的那一次（续接被拒、停在用量上限上）的花费并进最终结果：两次都是这条运行的花费，用量、成本与时长都按两次相加记账。
    /// 两次的用量此前已各自折算成这一轮的（被拒的那次续接了会话，重试那次没有），这里只相加。
    /// </summary>
    private static CliAgentResult CombineWithEarlierAttempt(CliAgentResult earlier, CliAgentResult final)
        => final with
        {
            DurationMs = earlier.DurationMs + final.DurationMs,
            Usage = earlier.Usage.Count == 0 ? final.Usage : CliTurnUsage.Sum(earlier.Usage, final.Usage)
        };

    /// <summary>
    /// 接着跑的那一次发给 agent 的话：它停在了上限上、换了账号接着来；已经返回结果的工具调用都生效了，不要再调一遍。
    /// </summary>
    internal const string UsageLimitContinuation =
        "NOTE: your previous attempt at this turn was stopped because the account it ran on reached its usage limit; " +
        "it now continues on another account. Carry on with the user's last message from where you stopped. " +
        "Tool calls that already returned a result have taken effect: do not make them again.";

    /// <summary>
    /// 停在用量上限上的这一次：问消费方是否换好了另一份登录（<see cref="ICliUsageLimitHandler"/>）。
    /// 没有实现、答「不」或者自己出错，都按不重试处理，这一轮以 UsageLimit 结束。
    /// </summary>
    private async Task<bool> SignsInElsewhereAsync(CliRun run, CliRunSetup setup, CliAttempt attempt, CancellationToken cancellationToken)
    {
        if (_usageLimitHandler is null)
        {
            return false;
        }

        try
        {
            var retry = await _usageLimitHandler.OnUsageLimitAsync(new CliUsageLimitContext
            {
                RunId = run.Id,
                AgentId = run.AgentId,
                ThreadId = run.ThreadId,
                CreatorId = run.CreatorId,
                ProviderKey = setup.Provider.Key,
                HostName = setup.Target.Name,
                ResetsAt = attempt.Result.UsageLimitResetsAt,
                Notice = attempt.Result.Error
            }, cancellationToken);

            if (retry)
            {
                _logger.LogInformation(
                    "CLI run {RunId} reached the account's usage limit after {ToolCalls} tool call(s); continuing once on another sign-in",
                    run.Id, attempt.ToolCalls);
            }

            return retry;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "CLI run {RunId}: the usage limit handler failed; ending the run on the usage limit", run.Id);
            return false;
        }
    }

    /// <summary>
    /// 停在用量上限上之后、换个登录接着跑之前：只记下非终态的事实，运行保持 Running（同 <see cref="PrepareFreshSessionRetryAsync"/>）。
    /// 返回接着跑时发给 agent 的话；null 表示照常重发这一轮的提示。
    /// </summary>
    /// <remarks>
    /// CLI 已经把这一轮的提示（以及上限之前做完的工具调用）写进了这一次的会话，所以接着跑续接的是<b>这一次</b>的会话，
    /// 只补一句说明：重发提示，agent 会看到同一个问题两遍；从上一轮重来，上限之前已经执行过的工具调用会再执行一遍。
    /// 这一次没报出会话 id（CLI 没走到建立会话那一步）时，会话里没有这一轮的任何东西，照常从原来的续接点重发提示。
    /// 会话指针要落库：接着跑被停机打断、运行回到队列时，重新执行续接的是同一个会话（那时重发的是完整提示）。
    /// </remarks>
    private async Task<string?> PrepareUsageLimitRetryAsync(CliRun run, CliAttempt stopped)
    {
        run.MessageCount = stopped.Sequence;
        string? continuation = null;
        if (!string.IsNullOrWhiteSpace(stopped.Result.SessionId))
        {
            run.ProviderSessionId = stopped.Result.SessionId;
            run.ResumeAtEntryId = null;
            run.ResumeExpected = true;
            continuation = UsageLimitContinuation;
        }

        var (sessionId, resumeAt, resumeExpected) = (run.ProviderSessionId, run.ResumeAtEntryId, run.ResumeExpected);
        await _runRepository.AcrossTenants()
            .Where(r => r.Id == run.Id && r.Status == CliRunStatus.Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.MessageCount, stopped.Sequence)
                .SetProperty(r => r.ProviderSessionId, sessionId)
                .SetProperty(r => r.ResumeAtEntryId, resumeAt)
                .SetProperty(r => r.ResumeExpected, resumeExpected), CancellationToken.None);
        return continuation;
    }

    /// <summary>
    /// 在本轮上下文尾部声明「上一轮的对话历史已丢失」。
    /// </summary>
    /// <remarks>
    /// 不说的话,新会话里的 agent 会自然地假装连续("如我之前所说…"),
    /// 而用户根本看不出这一轮其实是从零开始的 —— 那比直接报错更糟。
    /// </remarks>
    private static string AppendContextLossNotice(string? perTurnContext)
    {
        const string notice =
            "NOTE: the previous conversation session could not be resumed, so none of the earlier " +
            "turns are available to you. Answer from this message alone, and say so if the user " +
            "refers to something you cannot see.";
        if (string.IsNullOrWhiteSpace(perTurnContext))
        {
            return notice;
        }

        // 截断不了而开新会话、新会话又被拒绝重试时会走到这里两次：说一遍就够了
        return perTurnContext.Contains(notice, StringComparison.Ordinal)
            ? perTurnContext
            : perTurnContext + Environment.NewLine + Environment.NewLine + notice;
    }

    /// <summary>
    /// 问执行目标与消费方的 contributor 这次运行怎样启动（权限、内置工具、MCP 合并、设置来源、配置目录……）。
    /// </summary>
    /// <remarks>
    /// 远程运行时上的运行，档案定下之后再收一次：provider 没有允许把部署的兜底令牌交给远程目标时，<c>UseFallbackAuthToken</c> 恒为 false。
    /// 放在 contributor 之后，contributor 打不开它：服务器的凭据送进用户的工作区，在那里跑的人就读得到它。
    /// </remarks>
    private async Task<CliLaunchProfile> ResolveLaunchProfileAsync(
        CliRun run, CliRunSetup setup, CliWorkspace workspace, CliLaunchProfilePurpose purpose, CancellationToken cancellationToken)
    {
        var profile = await CliLaunchProfileResolver.ResolveAsync(setup.Target, _profileContributors, new CliLaunchProfileRequest
        {
            Scope = setup.Scope,
            Provider = setup.Provider,
            WorkDirectory = workspace.WorkDirectory,
            WorkDirectoryIsUserOwned = workspace.WorkDirectoryIsUserOwned
                                       || ResolveWorkDirectoryPolicy(run, setup).Mode == CliWorkDirectoryMode.UserProvided,
            Purpose = purpose
        }, cancellationToken);

        return setup.Runtime.Mode == CliRuntimeMode.RemoteDaemon && !setup.Provider.AllowFallbackAuthTokenOnRemoteTargets
            ? profile with { UseFallbackAuthToken = false }
            : profile;
    }

    private void RememberSession(string? sessionId)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            _sessionIds.Add(sessionId);
        }
    }

    /// <summary>
    /// 线程在这次运行期间被删除：删掉这一次碰过的会话在 CLI 配置目录里的记录（只在档位找得到它们时，见 <see cref="CliSessionStore"/>）。
    /// </summary>
    /// <remarks>
    /// 只是续接过的会话也删：删除事件的处理器可能已经先删了它，而进程随后又往里追加了这一轮。
    /// 尽力而为，失败只记日志：记录留在执行目标上，由 CLI 自己按保留期清理。
    /// </remarks>
    private async Task RemoveSessionRecordsAsync(CliRun run, ICliHostTarget target)
    {
        if (_setup is not { } setup || _sessionIds.Count == 0
            || setup.Provider.UserConfigIsolation != CliUserConfigIsolation.ProvidedConfigDirectory)
        {
            return;
        }

        try
        {
            var profile = _launchProfile ?? await ResolveLaunchProfileAsync(
                run, setup, UnmanagedWorkspace(run), CliLaunchProfilePurpose.SessionCleanup, CancellationToken.None);
            var paths = _sessionIds.SelectMany(id => CliSessionStore.Locate(setup.Provider, profile, target.FileSystem.Paths, id));
            var removed = await CliSessionStore.DeleteAsync(target.FileSystem, paths, CancellationToken.None);
            _logger.LogInformation(
                "Removed {Count} session record(s) of CLI run {RunId}: thread {ThreadId} was deleted while it ran", removed, run.Id, run.ThreadId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CliHostUnavailableException or CliProviderConfigurationException)
        {
            _logger.LogWarning(ex, "Could not remove the session records of CLI run {RunId} on {Host}", run.Id, target.Name);
        }
    }

    /// <summary>这一轮有没有人能回答：启动档案说了算，没说就按调用方给的。</summary>
    private static bool IsAttended(CliLaunchProfile profile, CliRun run) => profile.Attended ?? run.Attended;

    /// <summary>agent 的问题与计划交不交给使用方：有人值守，且 provider 能让 CLI 把它们交给框架。与档位无关。</summary>
    private static bool RelaysInteractions(CliProviderDescriptor provider, bool attended)
        => attended && provider.HostPromptArgs is { Count: > 0 };

    /// <summary>
    /// 档案收窄了内置工具、而这一轮转发交互请求时，把 provider 的交互工具补进那份清单（档案没收窄或没人值守时原样返回）。
    /// </summary>
    /// <remarks>
    /// 消费方收窄工具是为了限定 agent 能做的事（只许联网搜索之类），问用户、交计划不在它想拿掉的范围里：
    /// 拿掉了，有人值守的一轮问不了也交不了计划（计划档里 agent 只能把计划写进回复然后停下）。没有人值守时它们本就不该在，不补。
    /// </remarks>
    internal static CliLaunchProfile WithInteractionTools(CliLaunchProfile profile, CliProviderDescriptor provider, CliRun run)
    {
        if (profile.BuiltInTools is not { } tools || provider.InteractionTools.Count == 0
            || !RelaysInteractions(provider, IsAttended(profile, run)))
        {
            return profile;
        }

        var missing = provider.InteractionTools.Where(t => !tools.Contains(t, StringComparer.Ordinal)).ToList();
        return missing.Count == 0 ? profile : profile with { BuiltInTools = [.. tools, .. missing] };
    }

    /// <summary>provider 能在会话里换强度时，交接不因它不同而另起进程的开关（适配器接手时换成这一轮的值）。</summary>
    private static readonly string[] InSessionOptions = ["--effort"];

    private CliAgentLaunchContext BuildLaunchContext(
        CliRun run, CliRunSetup setup, CliWorkspace workspace, CliLaunchEnvironment launchEnvironment, CliLaunchProfile profile,
        CliAgentOptions options, string? continuation = null)
    {
        // 这一轮的 CLI 不是绑定的那家时，绑定上按那家 CLI 写的参数、模型与推理强度不用（见 BindingProviderMatchesAsync）
        var customArgs = setup.BindingProviderMatches ? ParseArgs(setup.Binding.CustomArgsJson) : [];
        string[] extraArgs = [.. setup.Provider.ExtraArgs];
        var bindingModel = setup.BindingProviderMatches ? setup.Binding.Model : null;

        // 用户为这一轮选了模型：它说了算。绑定的自定义参数、部署级参数里写的 --model 会排在框架给的那个之后、把它盖掉，所以去掉
        if (!string.IsNullOrWhiteSpace(run.Model))
        {
            customArgs = WithoutOption(customArgs, ModelOption);
            extraArgs = [.. WithoutOption(extraArgs, ModelOption)];
        }

        // 每轮易变的上下文跟着提示走（缺省追加在<b>提示尾部</b>，斜杠命令时适配器可能单独投递），绝不进 brief：
        // brief 在缓存前缀里，一变就作废整段历史的 prompt cache。
        // 接着上一次的会话跑时，提示与本轮上下文都已经在会话里，只发那句说明
        var prompt = continuation ?? run.Prompt;
        var turnContext = continuation is null && !string.IsNullOrWhiteSpace(run.PerTurnContext) ? run.PerTurnContext : null;

        var attended = IsAttended(profile, run);

        return new CliAgentLaunchContext
        {
            Provider = setup.Provider,
            ExecutablePath = setup.ExecutablePath,
            Prompt = prompt,
            TurnContext = turnContext,
            BackgroundTaskWait = options.BackgroundTaskWait,
            WorkingDirectory = workspace.WorkDirectory,
            Model = !string.IsNullOrWhiteSpace(run.Model) ? run.Model
                : string.IsNullOrWhiteSpace(bindingModel) ? setup.Provider.DefaultModel : bindingModel,
            // 用户为这一轮选的强度覆盖绑定上的（它已按这一轮的模型校验过）
            ThinkingLevel = !string.IsNullOrWhiteSpace(run.Effort) ? run.Effort
                : setup.BindingProviderMatches ? setup.Binding.ThinkingLevel : null,
            ResumeSessionId = run.ProviderSessionId,
            ResumeAtEntryId = string.IsNullOrWhiteSpace(run.ProviderSessionId) ? null : run.ResumeAtEntryId,
            InlineSystemPrompt = ResolveInlineSystemPrompt(run, setup, workspace),
            McpConfigPath = workspace.McpConfigPath,
            McpConfigJson = workspace.McpConfigJson,
            PermissionMode = profile.PermissionMode,
            // 有人值守才有人能回答：没有人时「先问我」按只读档当场拒绝，不让每个请求挂到超时；
            // 计划档获批之后的改动同样逐个问
            AsksForApproval = profile.PermissionMode is CliPermissionMode.Ask or CliPermissionMode.Plan && attended,
            // agent 问用户问题、交出计划等批准：只要有人值守、provider 能把它们交给框架，就交给使用方（与档位无关）
            RelaysInteractions = RelaysInteractions(setup.Provider, attended),
            StrictMcpConfig = profile.StrictMcpConfig,
            // 隔离与档案带来的参数排在部署级 ExtraArgs 之前：档案没接管的开关，部署方写在后面的那个生效。
            ManagedArgs = launchEnvironment.Args,
            OwnedArgs = launchEnvironment.OwnedArgs,
            ExtraArgs = extraArgs,
            CustomArgs = customArgs,
            Environment = launchEnvironment.Environment,
            InheritAllHostEnvironment = options.InheritAllHostEnvironment,
            EnvironmentWhitelist = options.EnvironmentWhitelist,
            HandshakeTimeout = options.HandshakeTimeout,
            TerminateGrace = options.TerminateGrace
        };
    }

    /// <summary>
    /// 要内联进提示的 agent 说明。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>provider 没有原生记忆文件（<see cref="CliProviderDescriptor.RequiresInlineSystemPrompt"/>）：每一轮都带。</item>
    /// <item>工作目录是用户提供的、说明却没能写成追加系统提示文件（provider 不支持）：只在新会话的第一条提示里带。
    /// 布置器不往用户目录里写记忆文件；续接的会话里第一轮带过的说明还在，再带一次只是重复占上下文。
    /// ACP 没有会话级的系统提示参数，内建 ACP provider 都走这一条。</item>
    /// </list>
    /// </remarks>
    private string? ResolveInlineSystemPrompt(CliRun run, CliRunSetup setup, CliWorkspace workspace)
    {
        if (!setup.Binding.InjectAgentInstructions)
        {
            return null;
        }

        if (setup.Provider.RequiresInlineSystemPrompt)
        {
            return _briefComposer.Compose(setup.Agent, setup.Provider);
        }

        return workspace.WorkDirectoryIsUserOwned && workspace.SystemPromptFilePath is null
                                                  && string.IsNullOrWhiteSpace(run.ProviderSessionId)
            ? _briefComposer.Compose(setup.Agent, setup.Provider)
            : null;
    }

    private const string ModelOption = "--model";

    /// <summary>去掉参数表里的某个带值选项（<c>--opt value</c> 与 <c>--opt=value</c> 两种写法）。</summary>
    private static List<string> WithoutOption(IReadOnlyList<string> arguments, string option)
    {
        var result = new List<string>(arguments.Count);
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            if (string.Equals(argument, option, StringComparison.Ordinal))
            {
                i++; // 连同它的取值一起去掉
                continue;
            }

            if (argument.StartsWith(option + "=", StringComparison.Ordinal))
            {
                continue;
            }

            result.Add(argument);
        }

        return result;
    }

    /// <summary>
    /// 解析生效的空闲阈值。每 agent 的覆盖<b>只允许收紧</b>：
    /// 允许放宽等于让任意一个 agent 自己解除全局安全边界。
    /// </summary>
    private static TimeSpan ResolveIdleWatchdog(CliAgentBinding binding, CliAgentOptions options)
        => binding.IdleWatchdog is { } perAgent && perAgent > TimeSpan.Zero && perAgent < options.IdleWatchdog
            ? perAgent
            : options.IdleWatchdog;

    /// <summary>批准通道写结局事件：与主循环同一份落库，写完提示订阅者。</summary>
    private async Task PersistAndSignalAsync(CliRun run, CliAgentEvent evt, int sequence, CliToolResultOptions limits)
    {
        await PersistEventAsync(run, evt, sequence, limits, CancellationToken.None);
        _signalHub.Signal(run.Id);
    }

    /// <summary>一次尝试结束时收掉还没写给 CLI 的权限请求。失败只记日志：终态与收尾不能因为这一步落不了库而走不下去。</summary>
    private async Task CloseApprovalsAsync(CliRun run, CliRunApprovals approvals)
    {
        try
        {
            await approvals.CloseAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not close the open permission requests of CLI run {RunId}", run.Id);
        }
    }

    /// <summary>
    /// 一条事件落库。写不进去（数据库拒收这一行）时这次运行照常往下走：在它的序号上留一条说明，而不是让异常带走整轮。
    /// </summary>
    /// <remarks>
    /// 事件流是记录，不是这次运行本身：CLI 已经做完的工作（工作区里的改动、已经花掉的用量）不会因为记不下一条事件而撤销，
    /// 让异常冒上去只会把这一轮的回复、终态与结算一起丢掉，使用方看到的是一句「An error occurred while saving the entity changes」。
    /// 每条事件在自己的作用域里写，失败的那一行不会留在任何还要再保存的上下文里，后面的事件不受牵连。
    /// 只接 <see cref="DbUpdateException"/>（这一行被拒）；取消照常抛出，连接层面的失败也照常抛出 —— 那时后面的写也不会成功，
    /// 运行按失败收尾才是对的。
    /// </remarks>
    private async Task PersistEventAsync(
        CliRun run, CliAgentEvent evt, int sequence, CliToolResultOptions limits, CancellationToken cancellationToken)
    {
        try
        {
            await InsertEventAsync(run, evt, sequence, limits, cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Could not record event {Sequence} ({Type}) of CLI run {RunId}; the run goes on without it",
                sequence, evt.Type, run.Id);

            var notice = new CliAgentEvent
            {
                Type = CliAgentEventType.Log,
                Level = UnrecordedEventLevel,
                Content = $"A {evt.Type} event could not be recorded and is missing here."
            };
            try
            {
                await InsertEventAsync(run, notice, sequence, limits, cancellationToken);
            }
            catch (DbUpdateException noticeEx)
            {
                _logger.LogWarning(noticeEx, "Could not record the notice for the missing event {Sequence} of CLI run {RunId} either",
                    sequence, run.Id);
            }
        }
    }

    /// <summary>代替一条没记下来的事件的那条说明的日志级别。</summary>
    internal const string UnrecordedEventLevel = "warning";

    private async Task InsertEventAsync(
        CliRun run, CliAgentEvent evt, int sequence, CliToolResultOptions limits, CancellationToken cancellationToken)
    {
        // 每条事件在自己的作用域里写：这个执行器的作用域（与其中的 DbContext）活过整条运行，
        // 写进去的事件行会一直被跟踪，每次保存都要扫一遍此前的全部事件（DetectChanges 与审计填充），
        // 一条运行累计 O(n²)，内存也随事件数线性增长；ACP 每个流式分块就是一条事件，长任务动辄数万条。
        // 新作用域写完即释放，什么都不留在长寿命的上下文里。租户覆盖是静态 AsyncLocal，随调用链进入新作用域。
        using var scope = _scopeFactory.CreateScope();

        var blocks = evt.OutputBlocks is { Count: > 0 }
            ? await StoreToolImagesAsync(scope.ServiceProvider, run, evt, LimitText(evt.OutputBlocks, ToolOutputMaxLength), limits, cancellationToken)
            : null;

        var message = new CliRunMessage
        {
            TenantId = run.TenantId,
            RunId = run.Id,
            Sequence = sequence,
            Type = evt.Type,
            Content = evt.Content,
            Tool = evt.Tool,
            CallId = evt.CallId,
            ParentCallId = evt.ParentCallId,
            InputJson = CliRunMessageConverter.SerializePayload(evt),
            Output = Truncate(evt.Output, ToolOutputMaxLength),
            OutputBlocksJson = CliRunMessageConverter.SerializeBlocks(blocks),
            Status = evt.Status,
            Level = evt.Level,
            PermissionJson = evt.Permission is null ? null : JsonSerializer.Serialize(evt.Permission, TnziJsonDefaults.Options)
        };

        var repository = scope.ServiceProvider.GetRequiredService<IRepository<CliRunMessage, Guid>>();
        await repository.InsertAsync(message, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// 工具结果的名字：stream-json 的 tool_result 只带调用 id，名字在之前那条 ToolUse 上。记下来补上，
    /// 好让每一条结果事件自己就说得出是哪个工具的（重新接上流、管理端事件列表都不必回头找发起事件）。
    /// </summary>
    private static CliAgentEvent NameToolResult(CliAgentEvent evt, Dictionary<string, string> toolNames)
    {
        if (string.IsNullOrEmpty(evt.CallId))
        {
            return evt;
        }

        if (evt.Type == CliAgentEventType.ToolUse && !string.IsNullOrEmpty(evt.Tool))
        {
            toolNames[evt.CallId] = evt.Tool;
            return evt;
        }

        return evt.Type == CliAgentEventType.ToolResult && string.IsNullOrEmpty(evt.Tool)
               && toolNames.TryGetValue(evt.CallId, out var name)
            ? evt with { Tool = name }
            : evt;
    }

    /// <summary>
    /// 把工具结果里的图片存成附件、换成附件 id：只存认得出的图片类型（PNG / JPEG / GIF / WebP），且受
    /// <see cref="CliToolResultOptions"/> 的大小与张数限制；不存的那张在原处换成一行说明，不悄悄消失。
    /// </summary>
    private async Task<List<CliContentBlock>> StoreToolImagesAsync(
        IServiceProvider services, CliRun run, CliAgentEvent evt, List<CliContentBlock> blocks,
        CliToolResultOptions limits, CancellationToken cancellationToken)
    {
        var attachments = services.GetService<IAgentAttachmentService>();
        var result = new List<CliContentBlock>(blocks.Count);
        var stored = 0;

        foreach (var block in blocks)
        {
            if (block.Type != CliContentBlock.ImageType)
            {
                result.Add(block);
                continue;
            }

            var (id, note) = await TryStoreToolImageAsync(attachments, run, evt, block, limits, stored, cancellationToken);
            if (id is null)
            {
                result.Add(new CliContentBlock { Type = CliContentBlock.TextType, Text = note });
                continue;
            }

            stored++;
            result.Add(block with { Data = null, AttachmentId = id });
        }

        return result;
    }

    /// <summary>存一张工具图片，返回附件 id；存不了时返回写给用户看的那行说明。</summary>
    private static async Task<(Guid? Id, string? Note)> TryStoreToolImageAsync(
        IAgentAttachmentService? attachments, CliRun run, CliAgentEvent evt, CliContentBlock block,
        CliToolResultOptions limits, int alreadyStored, CancellationToken cancellationToken)
    {
        if (attachments is null || string.IsNullOrWhiteSpace(block.Data))
        {
            return (null, "[image not stored]");
        }

        if (alreadyStored >= limits.MaxImagesPerResult)
        {
            return (null, $"[image omitted: a tool result keeps at most {limits.MaxImagesPerResult} images]");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(block.Data);
        }
        catch (FormatException)
        {
            return (null, "[image omitted: the tool returned invalid image data]");
        }

        if (bytes.LongLength > limits.MaxImageBytes)
        {
            return (null, $"[image omitted: {bytes.LongLength / 1024} KB is larger than the {limits.MaxImageBytes / 1024} KB limit]");
        }

        var mediaType = ChatAttachmentRules.DetectMediaType(bytes);
        if (mediaType is null || !mediaType.StartsWith("image/", StringComparison.Ordinal))
        {
            return (null, $"[image omitted: unsupported image type {block.MediaType ?? "unknown"}]");
        }

        var ids = await attachments.SaveAsync([new NewAgentAttachment
        {
            Origin = AttachmentOrigin.ToolResult,
            MediaType = mediaType,
            Content = bytes,
            ThreadId = run.ThreadId,
            RunId = run.Id,
            ToolCallId = evt.CallId,
            OwnerId = run.CreatorId,
            TenantId = run.TenantId
        }], cancellationToken);

        return (ids[0], null);
    }

    /// <summary>
    /// 工具结果的文本块按与 <see cref="CliRunMessage.Output"/> 相同的总上限截断：超出的部分截掉并注明截掉了多少，
    /// 之后的文本块不再保留（图片块照常处理）。
    /// </summary>
    internal static List<CliContentBlock> LimitText(IReadOnlyList<CliContentBlock> blocks, int maxLength)
    {
        var remaining = maxLength;
        var result = new List<CliContentBlock>(blocks.Count);
        foreach (var block in blocks)
        {
            if (block.Type != CliContentBlock.TextType || block.Text is null)
            {
                result.Add(block);
                continue;
            }

            if (remaining <= 0)
            {
                continue;
            }

            var text = Truncate(block.Text, remaining)!;
            remaining -= block.Text.Length;
            result.Add(block with { Text = text });
        }

        return result;
    }

    /// <summary>
    /// 这次运行的上传附件（随用户消息存档的那些），最早的在前，分成随提示投递的（图片与 PDF）与文件（其余，位置取自启动档案）。
    /// </summary>
    private async Task<(IReadOnlyList<CliPromptAttachment> Inline, IReadOnlyList<CliPromptFile> Files)> LoadAttachmentsAsync(
        Guid runId, CliLaunchProfile profile, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var attachments = scope.ServiceProvider.GetService<IAgentAttachmentService>();
        if (attachments is null)
        {
            return ([], []);
        }

        var stored = await attachments.GetForRunAsync(runId, AttachmentOrigin.Upload, cancellationToken);
        var inline = stored
            .Where(a => ChatAttachmentRules.IsInlineMediaType(a.MediaType))
            .Select(a => new CliPromptAttachment(a.MediaType, a.FileName, a.Content))
            .ToList();
        var files = stored
            .Where(a => !ChatAttachmentRules.IsInlineMediaType(a.MediaType))
            .Select(a => new CliPromptFile(
                a.Id,
                string.IsNullOrWhiteSpace(a.FileName) ? ChatAttachmentRules.DefaultFileName : a.FileName,
                a.MediaType,
                a.Size,
                profile.AttachmentPaths.TryGetValue(a.Id, out var path) && !string.IsNullOrWhiteSpace(path) ? path : null))
            .ToList();
        return (inline, files);
    }

    private async Task CompleteAsync(CliRun run, CliRunSetup setup, CliAttempt attempt, CancellationToken cancellationToken)
    {
        var result = attempt.Result;
        var sequence = attempt.Sequence;

        // resume 被拒时适配器已经把 SessionId 清空了；这里保持它的判断，
        // 不要拿流里看到的旧 id 覆盖回去 —— 那个 id 已经确定用不了了。
        var providerSessionId = result.ResumeRejected ? null : result.SessionId ?? attempt.SessionId;

        // 用量与成本都是这一轮的（适配器报的会话累计值已在 RunProcessAsync 里折算掉）；累计值单独存，给下一轮当基线
        var modelCosts = ResolveModelCosts(setup, result);
        string? usageJson = null;
        decimal? estimatedCost = null;
        if (result.Usage.Count > 0)
        {
            usageJson = JsonSerializer.Serialize(result.Usage, TnziJsonDefaults.Options);
            estimatedCost = modelCosts.Values.Any(c => c.HasValue)
                ? modelCosts.Values.Sum(c => c ?? 0m)
                : null;
        }

        var sessionUsageJson = attempt.SessionTotals is { Count: > 0 } totals
            ? JsonSerializer.Serialize(totals, TnziJsonDefaults.Options)
            : null;

        // 回复先于终态写进线程：等终态的流式端点与门面看到「结束」的那一刻，回复消息已经在线程里、id 已经在运行记录上。
        // 进程随这次运行结束时（没有交给同线程的下一条运行），还在跑的子 agent 随之停止
        var reply = await WriteReplyAsync(run, result, processEnds: _handoff is null);

        var finished = await _finalizer.FinishAsync(run.Id, new CliRunTermination(result.Status, result.FailureReason, result.Error)
        {
            Output = result.Output,
            DurationMs = result.DurationMs,
            UsageLimitResetsAt = result.FailureReason == CliRunFailureReason.UsageLimit ? result.UsageLimitResetsAt?.UtcDateTime : null,
            Completion = new CliRunCompletion(sequence, providerSessionId, usageJson, estimatedCost)
            {
                ProviderSessionEntryId = result.ResumeRejected ? null : result.SessionEntryId,
                AssistantMessageId = reply?.MessageId ?? _lastTurnMessageId,
                SessionUsageJson = sessionUsageJson,
                Context = result.Context
            }
        });

        // 内存里的记账本照这一次的结果更新：用量按这一次真实的花费记，无论终态是不是这一次写下的。
        run.Status = result.Status;
        run.FailureReason = result.FailureReason;
        run.Output = result.Output;
        run.Error = result.Error;
        run.DurationMs = result.DurationMs;
        run.MessageCount = sequence;
        run.ProviderSessionId = providerSessionId;

        if (!finished)
        {
            // 别的写入者先把它结束了（取消之后的租约回收、租约过期后另一个宿主跑完）：终态以先到者为准，
            // 这一次的结果不再写进运行记录，但花费是真实发生的，下面照记用量。刚写进线程的回复撤掉：
            // 记下的结局里没有它，留着它线程就与运行记录各说各话。
            _logger.LogWarning(
                "CLI run {RunId} finished with status {Status} here, but it had already been finished elsewhere; keeping the recorded outcome",
                run.Id, result.Status);
            await WithdrawReplyAsync(run, reply);
        }

        // 线程已删除时用量照记（花费是真实发生的），只是不再挂在那条线程上：与核心删除线程时对既有用量行的处理一致
        var usageThreadId = await IsThreadDeletedAsync(run) ? null : run.ThreadId;
        await LogUsageAsync(run, setup, result, modelCosts, usageThreadId, CancellationToken.None);

        if (finished)
        {
            _logger.LogInformation(
                "CLI run {RunId} finished with status {Status} in {DurationMs}ms ({Provider})",
                run.Id, run.Status, run.DurationMs, setup.Provider.Key);
        }
    }

    /// <summary>
    /// 把这次运行的回复写进线程：回复文本与工具调用都从落库的事件流折叠出来（与流式时客户端看到的同一个算法），
    /// 工具返回的图片挂到这条回复上。返回写下的消息；没写时为 null。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ 写在执行器里而不是在发起请求的那一端：外部运行动辄几分钟，客户端刷新、断线、关掉页面都是常态，
    /// 运行照样跑完。此前回复由门面在流结束时写，客户端一走，回复就只留在运行记录里，线程里只剩用户自己的提问。
    /// </para>
    /// <para>
    /// 完成的运行写回复（文本没有时用交付物）。被用户停下、失败、超时的运行，只要已经写出了文字或调用过工具，
    /// 也把那一部分写下来并标上 <see cref="ThreadMessageStatuses"/>：那是用户当时看到的，也是 CLI 会话里已经有的；
    /// 什么都没写出的不留空回复。
    /// </para>
    /// <para>
    /// 写入失败只记日志：回复写不进去不该让运行的终态写不下去。
    /// </para>
    /// </remarks>
    private async Task<WrittenReply?> WriteReplyAsync(CliRun run, CliAgentResult result, bool processEnds = false)
    {
        if (run.ThreadId is not { } threadId || await IsThreadDeletedAsync(run))
        {
            return null;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var services = scope.ServiceProvider;
            var threads = services.GetService<IAgentThreadInternalService>();
            var messages = services.GetService<IRepository<AgentThreadMessage, Guid>>();
            if (threads is null || messages is null)
            {
                return null;
            }

            var (reply, previousTurnText, turnMessageIds) = await FoldReplyAsync(services, run.Id);
            if (processEnds)
            {
                reply.SettleTasks();
            }

            // 后台子 agent 在后面几轮里的进展与结局记在派出它的那一轮：先把那几条消息的工具调用改成现在的样子
            await RewriteEarlierTurnsAsync(messages, reply, turnMessageIds);

            var text = reply.Text;

            // 交付物兜底只用这一轮自己的：等后台任务到了上限、最后一轮什么都没说时，交付物还是上一轮的回答，那一轮已经写过了
            if (result.Status == CliRunStatus.Completed && string.IsNullOrWhiteSpace(text)
                && !string.Equals(result.Output, previousTurnText, StringComparison.Ordinal))
            {
                text = result.Output ?? string.Empty;
            }

            var toolCalls = reply.ToolCalls;
            var usageLimit = result.FailureReason == CliRunFailureReason.UsageLimit;

            // 一轮只压缩了上下文（/compact）：没有回答，留一条空的「已压缩」回复，重新加载后对话里照样看得见
            var compacted = result.Status == CliRunStatus.Completed && result.Compaction is not null
                            && string.IsNullOrWhiteSpace(text) && toolCalls.Count == 0;

            // 用量上限是一个要让人看见的结局（什么时候能再用），即使一个字都没写出也留一条回复，重新加载后对话里照样有它
            if (string.IsNullOrWhiteSpace(text) && toolCalls.Count == 0 && !usageLimit && !compacted)
            {
                return null;
            }

            var status = result.Status switch
            {
                CliRunStatus.Completed when compacted => ThreadMessageStatuses.Compacted,
                CliRunStatus.Completed => null,
                CliRunStatus.Cancelled => ThreadMessageStatuses.Stopped,
                _ when usageLimit => ThreadMessageStatuses.UsageLimit,
                _ => ThreadMessageStatuses.Failed
            };
            var resetsAt = usageLimit ? result.UsageLimitResetsAt?.UtcDateTime : null;

            var messageId = SequentialGuid.NewGuid();
            var saved = await threads.SaveMessageAsync(
                threadId,
                MessageRole.Assistant,
                text,
                toolCalls: toolCalls.Count > 0 ? JsonSerializer.Serialize(toolCalls, TnziJsonDefaults.Options) : null,
                usage: SummarizeUsage(result.Usage),
                messageId: messageId,
                ct: CancellationToken.None);
            if (saved == Guid.Empty)
            {
                return null;
            }

            if (status is not null)
            {
                await messages.AsQueryable()
                    .IgnoreQueryFilters()
                    .Where(m => m.Id == saved)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(m => m.Status, status)
                        .SetProperty(m => m.UsageLimitResetsAt, resetsAt), CancellationToken.None);
            }

            if (services.GetService<IAgentAttachmentService>() is { } attachments)
            {
                await attachments.BindToMessageAsync(run.Id, AttachmentOrigin.ToolResult, saved, CancellationToken.None);
            }

            // 先于这一轮的结束事件落库：看着这条线程的页面从流里看到结束后才标已读，已读时间不会早于它
            await threads.MarkRepliedAsync(threadId, DateTime.UtcNow, CancellationToken.None);

            return new WrittenReply(saved, text);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CLI run {RunId} finished but its reply could not be written to thread {ThreadId}", run.Id, threadId);
            return null;
        }
    }

    /// <summary>
    /// 从落库的事件流折叠出这次运行<b>当前这一轮</b>的回复（包括被打回队列前那一次执行留下的事件）：最后一个
    /// <see cref="CliAgentEventType.TurnCompleted"/> 之前的那些轮次已经各自写成了一条消息。
    /// </summary>
    /// <returns>这一轮的回复，以及上一轮（最后一个 TurnCompleted）的交付物文本，没有上一轮时为 null。</returns>
    private static async Task<(CliReplyAccumulator Reply, string? PreviousTurnText, IReadOnlyList<Guid?> TurnMessageIds)> FoldReplyAsync(
        IServiceProvider services, Guid runId)
    {
        var events = await services.GetRequiredService<IRepository<CliRunMessage, Guid>>().AsQueryable()
            .IgnoreQueryFilters()
            .Where(m => m.RunId == runId)
            .OrderBy(m => m.Sequence)
            .ToListAsync(CancellationToken.None);

        var reply = new CliReplyAccumulator();
        string? previousTurnText = null;
        var turnMessageIds = new List<Guid?>();
        foreach (var message in events)
        {
            var evt = CliRunMessageConverter.ToEvent(message);
            if (message.Type == CliAgentEventType.TurnCompleted)
            {
                reply.BeginTurn();
                previousTurnText = message.Content;
                turnMessageIds.Add(evt.Turn?.AssistantMessageId);
                continue;
            }

            // 队列交给这一轮、被收下的用户消息：它之前的那段回答已经写成一条消息，之后的回答从头折叠
            if (evt.UserMessage is { Taken: true } taken)
            {
                reply.BeginTurn();
                turnMessageIds.Add(taken.AssistantMessageId);
                continue;
            }

            reply.Apply(evt);
        }

        return (reply, previousTurnText, turnMessageIds);
    }

    /// <summary>
    /// 重写前几轮里因为后来的事件（后台子 agent 的进展与结局）而变了的回复的工具调用。那一轮没写成消息的跳过；写失败只记日志。
    /// </summary>
    private async Task RewriteEarlierTurnsAsync(
        IRepository<AgentThreadMessage, Guid> messages, CliReplyAccumulator reply, IReadOnlyList<Guid?> turnMessageIds)
    {
        foreach (var turn in reply.EarlierTurnsChanged)
        {
            if (turn >= turnMessageIds.Count || turnMessageIds[turn] is not { } messageId)
            {
                continue;
            }

            var toolCalls = reply.ToolCallsOfTurn(turn);
            var json = toolCalls.Count > 0 ? JsonSerializer.Serialize(toolCalls, TnziJsonDefaults.Options) : null;
            try
            {
                await messages.AsQueryable()
                    .IgnoreQueryFilters()
                    .Where(m => m.Id == messageId)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.ToolCalls, json), CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "The tool calls of reply {MessageId} could not be brought up to date", messageId);
            }
        }
    }

    /// <summary>
    /// 运行中途结束的一轮：把它的回答写进线程（与运行结束时写最后一轮同一套折叠与写法），返回带上消息 id 的事件。
    /// 写不进去时事件照常落库，只是没有消息 id（写回复失败从不拦住运行）。
    /// </summary>
    private async Task<CliAgentEvent> WriteTurnReplyAsync(CliRun run, CliAgentEvent turn)
    {
        var reply = await WriteReplyAsync(run, new CliAgentResult { Status = CliRunStatus.Completed, Output = turn.Content });
        _lastTurnMessageId = reply?.MessageId ?? _lastTurnMessageId;
        return turn with { Turn = (turn.Turn ?? new CliTurnBoundary()) with { AssistantMessageId = reply?.MessageId } };
    }

    /// <summary>
    /// 重放中，停机前在此刻序号之后写给 CLI 的那些行原样再交给适配器（持有序号闸门）：先是记在这个序号上的排队消息，
    /// 再是占下一个序号的批准结局，交替到没有为止（排队消息记的是写它那一刻最后一个已分配的序号，所以排在下一个结局之前）。
    /// </summary>
    private static async Task ReplayRecordedWritesAsync(CliEventSequence sequence, CliRunQueuedInput? queuedInput, CliRunApprovals? approvals)
    {
        int before;
        do
        {
            before = sequence.Current;
            if (queuedInput is not null)
            {
                await queuedInput.ReplayRecordedAsync();
            }

            if (approvals is not null)
            {
                await approvals.ReplayRecordedAsync();
            }
        }
        while (sequence.Current != before);
    }

    /// <summary>
    /// 队列交给这一轮的一条消息有了着落。收下：收下之前的那段回答写进线程（与运行中途结束的一轮同一套折叠与写法，那段什么都没说时不写），
    /// 再由线程的消息队列把它写成一条用户消息（附件挂在上面），事件带上两条消息的 id。CLI 不收：它在队列里扣住、带上原因。
    /// 写不进去时事件照常落库，只是没有消息 id（写线程失败从不拦住运行）。
    /// </summary>
    private async Task<CliAgentEvent> RecordUserMessageAsync(CliRun run, CliAgentEvent evt)
    {
        if (evt.UserMessage is not { } message)
        {
            return evt;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var delivery = scope.ServiceProvider.GetService<IChatQueueDelivery>();
            if (!message.Taken)
            {
                if (delivery is not null)
                {
                    await delivery.HoldUntakenAsync(message.MessageId, message.Reason ?? "The agent did not take this message in.", CancellationToken.None);
                }

                return evt;
            }

            var reply = await WriteReplyAsync(run, new CliAgentResult { Status = CliRunStatus.Completed });
            _lastTurnMessageId = reply?.MessageId ?? _lastTurnMessageId;
            var threadMessageId = delivery is null || await IsThreadDeletedAsync(run)
                ? null
                : await delivery.MarkDeliveredAsync(message.MessageId, CancellationToken.None);
            return evt with { UserMessage = message with { AssistantMessageId = reply?.MessageId, ThreadMessageId = threadMessageId } };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CLI run {RunId}: the queued message {MessageId} it took in could not be written to its thread", run.Id, message.MessageId);
            return evt;
        }
    }

    /// <summary>记下这次尝试收不收得下运行中的消息（线程的消息队列据此决定「立即发送」停不停下这一轮）。</summary>
    private async Task RecordTakesMessagesAsync(CliRun run, bool takesMessages)
    {
        try
        {
            await _runRepository.AcrossTenants()
                .Where(r => r.Id == run.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.TakesMessagesMidTurn, (bool?)takesMessages), CancellationToken.None);
            run.TakesMessagesMidTurn = takesMessages;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CLI run {RunId}: could not record whether it takes messages while it runs", run.Id);
        }
    }

    /// <summary>这一轮的用量合计（写在回复消息上，与内建路径存的形状相同）。</summary>
    private static string? SummarizeUsage(IReadOnlyDictionary<string, CliAgentTokenUsage> usage)
    {
        if (usage.Count == 0)
        {
            return null;
        }

        var input = (int)usage.Values.Sum(u => u.InputTokens);
        var output = (int)usage.Values.Sum(u => u.OutputTokens);
        return JsonSerializer.Serialize(new TokenUsageDto
        {
            InputTokens = input,
            OutputTokens = output,
            TotalTokens = input + output,
            CachedInputTokens = (int)usage.Values.Sum(u => u.CacheReadTokens),
            CacheCreationTokens = (int)usage.Values.Sum(u => u.CacheWriteTokens)
        }, TnziJsonDefaults.Options);
    }

    /// <summary>撤掉一条已经写下、但运行的终态没认它的回复（终态被别处先写了）。</summary>
    private async Task WithdrawReplyAsync(CliRun run, WrittenReply? reply)
    {
        if (reply is null)
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var messages = scope.ServiceProvider.GetRequiredService<IRepository<AgentThreadMessage, Guid>>();
            await messages.EnsureTransactionStartedAsync(CancellationToken.None);
            await messages.AsQueryable()
                .IgnoreQueryFilters()
                .Where(m => m.Id == reply.MessageId)
                .ExecuteDeleteAsync(CancellationToken.None);

            if (scope.ServiceProvider.GetService<IRepository<AgentAttachment, Guid>>() is { } attachments)
            {
                await attachments.AsQueryable()
                    .IgnoreQueryFilters()
                    .Where(a => a.RunId == run.Id && a.Origin == AttachmentOrigin.ToolResult)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.MessageId, (Guid?)null), CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not withdraw the reply of CLI run {RunId} from thread {ThreadId}", run.Id, run.ThreadId);
        }
    }

    /// <summary>
    /// 按模型结算这一轮的成本（API 等价费用）：provider 自报值优先，没有才回落到按该模型的 token（含缓存读写）与配置的单价估算。
    /// </summary>
    /// <remarks>
    /// 自报值优先不是偏好问题：按 token 单价估算复现不了请求级定价规则
    /// （某些厂商在 prompt 超阈值后整请求翻倍），而一条用量记录聚合了一个 turn 里的多次调用，
    /// 事后无法判断哪一次踩了哪一档。claude 报的 <c>costUSD</c> 就是按 API 价目算的（含缓存读写），折算后是这一轮的那份。
    /// 某个模型用了 token 却报 0（CLI 不认识它的价目）时，那个模型按配置的单价估；单价也没配就是 null，这一轮在用量上限里少记它。
    /// <para>
    /// ★ 成本必须按模型拆开而不是只有一个总数：用量按模型逐行记账（预算与成本分析对这些行求和），
    /// 每行都填整轮总成本，一个带辅助小模型的 turn 就按模型数被成倍计入预算，预算在真实花费一半时就拒绝。
    /// 各行之和恒等于 <see cref="CliRun.EstimatedCostUsd"/>。
    /// </para>
    /// </remarks>
    private Dictionary<string, decimal?> ResolveModelCosts(CliRunSetup setup, CliAgentResult result)
        => result.Usage.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.ReportedCostUsd is > 0m
                ? entry.Value.ReportedCostUsd
                : _costCalculator?.CalculateCost(
                    setup.Provider.Key,
                    entry.Key,
                    entry.Value.InputTokens + entry.Value.CacheReadTokens + entry.Value.CacheWriteTokens,
                    entry.Value.OutputTokens,
                    entry.Value.CacheReadTokens,
                    entry.Value.CacheWriteTokens)
                  ?? entry.Value.ReportedCostUsd,
            StringComparer.Ordinal);

    private static decimal SumCost(IReadOnlyDictionary<string, decimal?> modelCosts) => modelCosts.Values.Sum(c => c ?? 0m);

    private static int ToInt(long value) => (int)Math.Clamp(value, 0, int.MaxValue);

    /// <summary>
    /// 按模型逐行写用量。每行只带<b>该模型自己的</b>成本（见 <see cref="ResolveModelCosts"/>）。
    /// </summary>
    /// <remarks>
    /// 时长每行照填本轮的墙钟时长：它是这一轮里每个模型实际所处的那段时间，分析端对它取平均而不求和，
    /// 重复不会被放大；拆分反而会让每个模型的平均时延变成一个没有物理含义的数。
    /// </remarks>
    private async Task LogUsageAsync(
        CliRun run, CliRunSetup setup, CliAgentResult result, IReadOnlyDictionary<string, decimal?> modelCosts,
        Guid? threadId, CancellationToken cancellationToken)
    {
        if (_usageLogService is null || result.Usage.Count == 0 || run.UsageMeteredElsewhere)
        {
            return;
        }

        foreach (var (model, usage) in result.Usage)
        {
            await _usageLogService.LogUsageAsync(new UsageLogEntry
            {
                OperationType = AIOperationType.AgentRun,
                Provider = setup.Provider.Key,
                Model = model,
                InputTokens = ToInt(usage.InputTokens),
                OutputTokens = ToInt(usage.OutputTokens),
                CachedInputTokens = ToInt(usage.CacheReadTokens),
                CacheCreationTokens = ToInt(usage.CacheWriteTokens),
                DurationMs = run.DurationMs,
                IsSuccess = run.Status == CliRunStatus.Completed,
                ErrorMessage = run.Error,
                // 运行在后台跑，没有登录用户：计在发起这次运行的人名下（与用量上限结算的是同一个人）
                UserId = run.CreatorId,
                AgentId = run.AgentId,
                ThreadId = threadId,
                EstimatedCostUsd = modelCosts.GetValueOrDefault(model)
            }, cancellationToken);
        }
    }

    private async Task FailAsync(
        CliRun run, CliRunFailureReason reason, string message, CancellationToken cancellationToken)
    {
        var finished = await _finalizer.FinishAsync(run.Id, new CliRunTermination(CliRunStatus.Failed, reason, message));

        run.Status = CliRunStatus.Failed;
        run.FailureReason = reason;
        run.Error = message;

        if (finished)
        {
            _logger.LogWarning("CLI run {RunId} failed ({Reason}): {Message}", run.Id, reason, message);
        }
        else
        {
            _logger.LogWarning(
                "CLI run {RunId} failed here ({Reason}: {Message}), but it had already been finished elsewhere; keeping the recorded outcome",
                run.Id, reason, message);
        }
    }

    /// <summary>
    /// 用户取消落终态。运行已经结束（<see cref="CompleteAsync"/> 已写过、或别处先结束了它）时什么都不做 ——
    /// 取消可能落在进程结束之后、方法返回之前的那几毫秒里。
    /// </summary>
    /// <remarks>
    /// 错误信息只在还没有的时候写：取消之前已经记下的原因留着。
    /// </remarks>
    private async Task MarkCancelledAsync(CliRun run)
    {
        if (IsTerminal(run.Status))
        {
            return;
        }

        var finished = await _finalizer.FinishAsync(
            run.Id,
            new CliRunTermination(CliRunStatus.Cancelled, CliRunFailureReason.Cancelled, CancelledMessage) { KeepExistingError = true });

        run.Status = CliRunStatus.Cancelled;
        run.FailureReason = CliRunFailureReason.Cancelled;
        run.Error ??= CancelledMessage;

        if (finished)
        {
            _logger.LogInformation("CLI run {RunId} cancelled on request", run.Id);
        }
    }

    /// <summary>
    /// 这个服务端是否在停机：执行器的令牌已取消（队列处理器的停机令牌），或应用已经开始停机。
    /// </summary>
    /// <remarks>
    /// 应用的 <c>ApplicationStopping</c> 先于各托管服务的 <c>StopAsync</c> 触发：消费方随停机关掉的远程通道可能在队列处理器的令牌
    /// 取消之前就让执行宿主连不上，只看令牌会把这条运行判成启动失败。
    /// </remarks>
    private bool IsHostStopping(CancellationToken cancellationToken)
        => cancellationToken.IsCancellationRequested || _applicationLifetime?.ApplicationStopping.IsCancellationRequested == true;

    /// <summary>
    /// 宿主停机：把运行打回队列。
    /// </summary>
    /// <remarks>
    /// 只改队列相关的四列，并且以「没有人要求取消」为条件 —— 整实体回写会把内存里
    /// 加载时的旧值（尤其是 <c>CancelRequested=false</c>）盖到别的副本刚写下的事实上；
    /// 条件不成立（取消请求恰好落在判断与写库之间）就改判终态，否则这一行会以
    /// <c>Queued + CancelRequested</c> 的形态永远躺在队列里：认领谓词跳过它，取消端点又只处理
    /// 它读到时还是 Queued 的行。
    /// </remarks>
    private async Task ReleaseToQueueAsync(CliRun run)
    {
        // 用 None：宿主已经在停机，带着已取消的令牌去写库只会让这条运行卡在 Dispatched。
        // 只放回还在进行中的行：已经落了终态的行不能被改回 Queued 再跑一遍。
        var released = await _runRepository.AcrossTenants()
            .Where(r => r.Id == run.Id && !r.CancelRequested
                        && (r.Status == CliRunStatus.Dispatched || r.Status == CliRunStatus.Running))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, CliRunStatus.Queued)
                .SetProperty(r => r.LeaseExpiresAt, (DateTime?)null)
                .SetProperty(r => r.ClaimedByHostId, (string?)null)
                .SetProperty(r => r.DispatchedAt, (DateTime?)null), CancellationToken.None);

        if (released == 0)
        {
            // 没放回去有两种原因：有人要求取消（落取消终态），或者这一行已经不在进行中（不再碰它）。
            if (await IsCancelRequestedAsync(run.Id))
            {
                await MarkCancelledAsync(run);
            }
            else
            {
                _logger.LogWarning("CLI run {RunId} was not released to the queue: it is no longer in progress", run.Id);
            }

            return;
        }

        _logger.LogInformation("CLI run {RunId} released back to the queue during host shutdown", run.Id);
    }

    /// <summary>句柄的长度上限（与 <c>CliRun.ReattachHandle</c> 的列宽一致）。</summary>
    private const int ReattachHandleMaxLength = 500;

    /// <summary>
    /// 把这个进程的重新附着句柄写进运行记录：从这一刻起，服务端停机时它留在执行宿主上接着跑。
    /// </summary>
    /// <remarks>
    /// 写不进去（句柄超长）时只记 Warning：这个进程照常跑，只是停机时按接不回来的进程处理（终止、打回队列）。
    /// </remarks>
    /// <summary>这一次进程的用量在不在别处计量（<see cref="CliLaunchEnvironment.UsageMeteredElsewhere"/>）：写进运行记录，接回来的进程沿用。</summary>
    private async Task RecordMeteringAsync(CliRun run, bool meteredElsewhere)
    {
        await _runRepository.AcrossTenants()
            .Where(r => r.Id == run.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.UsageMeteredElsewhere, meteredElsewhere), CancellationToken.None);
        run.UsageMeteredElsewhere = meteredElsewhere;
    }

    private async Task RecordReattachHandleAsync(CliRun run, string handle)
    {
        if (handle.Length > ReattachHandleMaxLength)
        {
            _logger.LogWarning(
                "The execution host gave CLI run {RunId} a reattach handle of {Length} characters (limit {Limit}); it cannot be reattached",
                run.Id, handle.Length, ReattachHandleMaxLength);
            return;
        }

        // 新的进程：上一个进程收过的排队消息与它无关
        var written = await _runRepository.AcrossTenants()
            .Where(r => r.Id == run.Id && r.Status == CliRunStatus.Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.ReattachHandle, handle)
                .SetProperty(r => r.HandedInputs, (string?)null), CancellationToken.None);
        if (written > 0)
        {
            run.ReattachHandle = handle;
            run.HandedInputs = null;
        }
    }

    /// <summary>这一次的进程已经结束：清掉它的句柄。</summary>
    /// <summary>
    /// 记下这条运行是不是只在等后台任务（<see cref="CliRun.AwaitingFollowUp"/>）：一轮在中途结束时置上，下一轮开始说话、
    /// 用工具、要回答或收下一条交来的消息时清掉。只在变化时写；写不进去只记日志（它只决定线程是不是标成「在跑」）。
    /// </summary>
    private async Task TrackFollowUpAsync(CliRun run, CliAgentEvent evt)
    {
        var awaiting = evt.Type switch
        {
            CliAgentEventType.TurnCompleted => true,
            CliAgentEventType.Text or CliAgentEventType.Thinking or CliAgentEventType.ToolUse
                or CliAgentEventType.ToolResult or CliAgentEventType.PermissionRequest => false,
            CliAgentEventType.UserMessage when evt.UserMessage is { Taken: true } => false,
            _ => run.AwaitingFollowUp
        };
        if (awaiting == run.AwaitingFollowUp)
        {
            return;
        }

        try
        {
            await _runRepository.AcrossTenants()
                .Where(r => r.Id == run.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.AwaitingFollowUp, awaiting), CancellationToken.None);
            run.AwaitingFollowUp = awaiting;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CLI run {RunId}: could not record whether it only waits on background tasks", run.Id);
        }
    }

    private async Task ClearReattachHandleAsync(CliRun run)
    {
        await _runRepository.AcrossTenants()
            .Where(r => r.Id == run.Id && r.ReattachHandle != null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.ReattachHandle, (string?)null)
                .SetProperty(r => r.HandedInputs, (string?)null), CancellationToken.None);
        run.ReattachHandle = null;
        run.HandedInputs = null;
    }

    /// <summary>
    /// 宿主停机，进程留在执行宿主上：运行保持 Running，租约立刻到期、谁也不占，好让重启后的（或者别的副本的）认领马上接它。
    /// </summary>
    /// <remarks>
    /// 不打回 Queued：Queued 会让同一线程的下一轮被认领，与还在跑的这个进程在同一个工作区里并发；也会让它被当成新运行从头再跑。
    /// 取消请求恰好在这时落下也照样留下：接回来的那一次执行先看取消标记，接上就停掉它。
    /// </remarks>
    private async Task LeaveForReattachAsync(CliRun run)
    {
        var now = DateTime.UtcNow;
        var left = await _runRepository.AcrossTenants()
            .Where(r => r.Id == run.Id && r.Status == CliRunStatus.Running && r.ReattachHandle != null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.LeaseExpiresAt, (DateTime?)now)
                .SetProperty(r => r.ClaimedByHostId, (string?)null)
                .SetProperty(r => r.DetachedAt, (DateTime?)now), CancellationToken.None);

        if (left == 0)
        {
            _logger.LogWarning(
                "CLI run {RunId} left its process on the execution host during shutdown, but the run is no longer in progress", run.Id);
            return;
        }

        _logger.LogInformation(
            "CLI run {RunId} left running on the execution host during shutdown; it will be reattached after restart", run.Id);
    }

    /// <summary>
    /// 接回服务端重启前起的那个进程。接回来了返回它；宿主说进程不在了，这一轮以 Interrupted 结束、返回 null。
    /// 眼下连不上时 <see cref="CliHostUnavailableException"/> 原样抛出（稍后再试）。
    /// </summary>
    private async Task<ICliReattachedProcess?> ReattachProcessAsync(CliRun run, CliRunSetup setup, CliAgentOptions options)
    {
        // 用 None：这一步是在问进程还在不在，回答之前不该因为停机而半途放弃；停机期间宿主的回答照样有用
        var result = await setup.Target.ReattachAsync(new CliReattachRequest
        {
            RunId = run.Id,
            Handle = run.ReattachHandle!,
            TerminateGrace = options.TerminateGrace
        }, CancellationToken.None);

        if (result.Process is not { } process)
        {
            await InterruptAsync(run, result.GoneReason ?? "The process of this run is no longer running on the execution host.");
            return null;
        }

        await _runRepository.AcrossTenants()
            .Where(r => r.Id == run.Id && r.Status == CliRunStatus.Running)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.DetachedAt, (DateTime?)null), CancellationToken.None);
        run.DetachedAt = null;

        _logger.LogInformation(
            "Reattached CLI run {RunId} to its process on {Host} ({StdinLines} stdin line(s) already delivered)",
            run.Id, setup.Target.Name, process.StdinLinesReceived);
        return process;
    }

    /// <summary>
    /// 等着接回进程的运行已经过了 <c>AI:Cli:Reattach:Timeout</c>，而它的运行时在一个这个副本不服务的远程执行目标上时，返回那个目标的标识；否则 null。
    /// </summary>
    /// <remarks>
    /// 这样的运行只会作为「没人能接」被认领（见 <c>CliRunQueueProcessor.TryClaimReattachAsync</c>）：服务它的副本早就该接走它了。
    /// </remarks>
    private async Task<string?> AbandonedOnUnservedTargetAsync(CliRun run, CliAgentOptions options)
    {
        var since = run.DetachedAt ?? run.LeaseExpiresAt;
        if (since is null || DateTime.UtcNow - since.Value < options.Reattach.Timeout)
        {
            return null;
        }

        var runtime = await _runtimeRepository.GetAsync(run.CliRuntimeId, CancellationToken.None);
        return runtime is { Mode: CliRuntimeMode.RemoteDaemon } && !_host.GetCoverage().Covers(runtime.Mode, runtime.HostId)
            ? runtime.HostId
            : null;
    }

    /// <summary>
    /// 执行宿主眼下连不上：隔一会儿再试；从断开算起过了 <c>AI:Cli:Reattach:Timeout</c> 还连不上，这一轮以 Interrupted 结束。
    /// </summary>
    private async Task RetryReattachLaterAsync(CliRun run, string reason, CliAgentOptions options)
    {
        var now = DateTime.UtcNow;
        var since = run.DetachedAt ?? now;
        var timeout = options.Reattach.Timeout;
        if (now - since >= timeout)
        {
            await InterruptAsync(run,
                $"The process of this run could not be reattached within {FormatDuration(timeout)}: {reason}");
            return;
        }

        var retryAt = now.Add(options.Reattach.RetryInterval);
        await _runRepository.AcrossTenants()
            .Where(r => r.Id == run.Id && r.Status == CliRunStatus.Running && r.ReattachHandle != null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.ClaimedByHostId, (string?)null)
                .SetProperty(r => r.LeaseExpiresAt, (DateTime?)retryAt), CancellationToken.None);

        _logger.LogInformation(
            "CLI run {RunId}: its execution host is not reachable ({Reason}); trying to reattach again at {RetryAt:O}",
            run.Id, reason, retryAt);
    }

    /// <summary>
    /// 进程接不回来：这一轮以 Interrupted 结束，不重跑。已经写出的部分（落库的事件）照常折成回复写进线程。
    /// </summary>
    /// <remarks>
    /// 会话指针不留：这一轮停在半截，下一轮从它之前那一轮截断续接（或开新会话），并在本轮上下文里说明这一轮停在了哪里（见 <see cref="ResolveResumePointerAsync"/>）。
    /// </remarks>
    private async Task InterruptAsync(CliRun run, string reason)
    {
        var sequence = await ResolveLastSequenceAsync(run, CancellationToken.None);
        var durationMs = run.StartedAt is { } startedAt ? (long)(DateTime.UtcNow - startedAt).TotalMilliseconds : run.DurationMs;
        var result = new CliAgentResult
        {
            Status = CliRunStatus.Failed,
            FailureReason = CliRunFailureReason.Interrupted,
            Error = reason,
            DurationMs = durationMs
        };

        var reply = await WriteReplyAsync(run, result, processEnds: true);
        var finished = await _finalizer.FinishAsync(
            run.Id,
            new CliRunTermination(CliRunStatus.Failed, CliRunFailureReason.Interrupted, reason)
            {
                DurationMs = durationMs,
                Completion = new CliRunCompletion(sequence, null, null, null) { AssistantMessageId = reply?.MessageId }
            },
            r => r.Status == CliRunStatus.Running);

        run.Status = CliRunStatus.Failed;
        run.FailureReason = CliRunFailureReason.Interrupted;
        run.Error = reason;
        run.MessageCount = sequence;

        if (!finished)
        {
            _logger.LogWarning("CLI run {RunId} was interrupted here, but it had already been finished elsewhere", run.Id);
            await WithdrawReplyAsync(run, reply);
            return;
        }

        _logger.LogWarning("CLI run {RunId} was interrupted: {Reason}", run.Id, reason);
    }

    /// <summary>接回来的运行的用量记账：不再开闸（进程早就在跑），只为结算准备一份预留。</summary>
    private UsageHold? ResumeUsageHold(CliRun run)
        => _usageLimits is not null && run.CreatorId is { } userId
            ? new UsageHold(new UsageLimitReservation(userId, run.StartedAt ?? DateTime.UtcNow))
            : null;

    /// <summary>
    /// 认出接回来的进程正在用的工作区，好在结束时照常收尾。一个文件都不写：进程正在读的 MCP 配置里有它的回写凭据。
    /// 认不出来（或者目标上读不了）时返回 null，收尾交给回收器。
    /// </summary>
    private async Task<CliWorkspace?> OpenWorkspaceAsync(CliRun run, CliRunSetup setup)
    {
        if (string.IsNullOrWhiteSpace(run.WorkDirectory))
        {
            return null;
        }

        var context = new CliRunContext
        {
            RunId = run.Id,
            AgentId = run.AgentId,
            TenantId = run.TenantId,
            ThreadId = run.ThreadId,
            UserId = run.CreatorId,
            Provider = setup.Provider,
            WorkDirectoryMode = ResolveWorkDirectoryPolicy(run, setup).Mode,
            UserWorkDirectory = ResolveWorkDirectoryPolicy(run, setup).UserWorkDirectory
        };

        try
        {
            return await _workspacePreparer.OpenAsync(setup.Target, run.WorkDirectory, context, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CliHostUnavailableException)
        {
            _logger.LogWarning(ex, "Could not open the workspace of reattached CLI run {RunId} at {WorkDirectory}", run.Id, run.WorkDirectory);
            return null;
        }
    }

    /// <summary>一个不受管的工作区描述：只有工作目录，没有要回滚的文件。认不出接回来的进程的工作区时用它起重试的进程。</summary>
    private static CliWorkspace UnmanagedWorkspace(CliRun run)
    {
        var directory = run.WorkDirectory ?? string.Empty;
        return new CliWorkspace
        {
            RootDirectory = directory,
            WorkDirectory = directory,
            OutputDirectory = directory,
            LogDirectory = directory
        };
    }

    /// <summary>
    /// 硬超时：接回来的进程从运行开始算起，已经跑掉的那段不重新计时（否则每次重启都给它续上一整段）。
    /// </summary>
    private static TimeSpan? ResolveHardTimeout(CliRun run, CliAgentOptions options, bool reattached)
    {
        if (options.HardTimeout is not { } hard || !reattached || run.StartedAt is not { } startedAt)
        {
            return options.HardTimeout;
        }

        var remaining = hard - (DateTime.UtcNow - startedAt);
        return remaining > MinimumRemainingHardTimeout ? remaining : MinimumRemainingHardTimeout;
    }

    /// <summary>接回来时硬超时已经用完：留一点时间让看门狗在下一个刻度上判超时、终止它，而不是把零交给看门狗。</summary>
    private static readonly TimeSpan MinimumRemainingHardTimeout = TimeSpan.FromSeconds(1);

    internal static string FormatDuration(TimeSpan duration)
        => duration.TotalMinutes >= 1 && duration.Seconds == 0
            ? $"{(int)duration.TotalMinutes} minute(s)"
            : $"{(int)duration.TotalSeconds} second(s)";

    /// <summary>
    /// 现读持久化的取消标记。标量投影绕开跟踪器的身份映射：执行器手里那份实体是取消之前加载的，
    /// 按实体查会原样拿回那份过期的 <c>false</c>。
    /// </summary>
    private Task<bool> IsCancelRequestedAsync(Guid runId)
        => _runRepository.AcrossTenants()
            .Where(r => r.Id == runId)
            .Select(r => r.CancelRequested)
            .FirstOrDefaultAsync(CancellationToken.None);

    private static bool IsTerminal(CliRunStatus status)
        => status is CliRunStatus.Completed or CliRunStatus.Failed
            or CliRunStatus.Cancelled or CliRunStatus.TimedOut;

    /// <param name="target">工作区所在的执行目标。</param>
    /// <param name="workspace">要收尾的工作区。</param>
    /// <param name="removeAll">
    /// 线程已删除时为 true，连目录一起删（用户提供的工作目录照旧不删）。平时只回滚受管写入、不删整个目录：
    /// 终态目录由 GC 按 TTL 回收，立刻删掉会让排障时连产物和日志都看不到。
    /// </param>
    private async Task CleanupWorkspaceAsync(ICliHostTarget target, CliWorkspace workspace, bool removeAll)
    {
        try
        {
            await _workspacePreparer.CleanupAsync(target, workspace, removeAll, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CliHostUnavailableException)
        {
            // 远程目标在运行结束前掉了线也走这里：清理是尽力而为，终态照常落库
            _logger.LogWarning(ex, "Could not clean up workspace at {Root} on {Host}", workspace.RootDirectory, target.Name);
        }
    }

    private static List<string> ParseArgs(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// 工具输出落库前的截断：正文最多 <paramref name="maxLength"/> 个码元，后面注明截掉了多少。
    /// </summary>
    /// <remarks>
    /// 截断点落在代理对中间时整个字符一起舍掉：孤立的高代理项会让严格 UTF-8 的驱动（Npgsql）拒绝写入这条事件。
    /// </remarks>
    internal static string? Truncate(string? value, int maxLength)
    {
        if (value is null || value.Length <= maxLength)
            return value;

        var head = value.FitToLength(maxLength);
        return head + $"\n… [truncated, {value.Length - head.Length} more characters]";
    }

    /// <param name="MessageId">写进线程的回复消息</param>
    /// <param name="Text">回复文本</param>
    private sealed record WrittenReply(Guid MessageId, string Text);

    /// <param name="RunId">已结束的那一轮</param>
    /// <param name="SessionId">它留下的会话</param>
    /// <param name="SessionEntryId">它在会话里的最后一个条目</param>
    /// <param name="UserMessageId">它回答的用户消息</param>
    /// <param name="CreationTime">它入队的时间</param>
    /// <param name="Interrupted">它是不是被中断的（这种轮次从不被续接，<paramref name="SessionId"/> 为 null）</param>
    private sealed record ResumeCandidate(
        Guid RunId, string? SessionId, string? SessionEntryId, Guid? UserMessageId, DateTime CreationTime, bool Interrupted);

    /// <param name="Prompt">那一轮的请求</param>
    /// <param name="Reason">它停下的原因（宿主给的）</param>
    /// <param name="ReplyText">它写出的文字（从落库的事件折出来）</param>
    /// <param name="ToolCalls">它调过几次工具</param>
    /// <param name="LastTool">最后调的那个工具</param>
    private sealed record InterruptedTurn(string Prompt, string? Reason, string ReplyText, int ToolCalls, string? LastTool);

    /// <param name="Result">这一次的结果；其中的用量已折算成这一轮的</param>
    /// <param name="SessionId">流里最后看到的会话 id</param>
    /// <param name="Sequence">这一次写完后的事件序号</param>
    /// <param name="SessionTotals">这一次结束时的会话累计用量（下一轮的基线）；协议不报累计值时为 null</param>
    /// <param name="ToolCalls">这一次发起的工具调用数（只用于日志：停在上限上时，已经返回的调用都已生效）</param>
    private sealed record CliAttempt(
        CliAgentResult Result, string? SessionId, int Sequence, IReadOnlyDictionary<string, CliAgentTokenUsage>? SessionTotals,
        int ToolCalls = 0)
    {
        /// <summary>这次运行在等后台任务时停下、要交给同线程下一条运行的进程（§6.18）；没有时为 null。</summary>
        public CliLiveSession? Handoff { get; init; }

        /// <summary>接过来的进程接不了这一轮（已经结束掉）：要另起进程续接同一会话。</summary>
        public bool ContinuationRefused { get; init; }
    }

    // Target：这次运行的执行目标，工作区、登录态查询、进程与清理都在它上面；Scope：取目标时用的身份，问启动档案时原样交出
    private sealed record CliRunSetup(
        CliAgentBinding Binding,
        Entities.CliRuntime Runtime,
        CliProviderDescriptor Provider,
        string ExecutablePath,
        Agent Agent,
        ICliHostTarget Target,
        CliHostScope Scope)
    {
        /// <summary>这次运行的 CLI 就是绑定的那家：绑定上的模型、推理强度与自定义参数适用。</summary>
        public bool BindingProviderMatches { get; init; } = true;
    }

    /// <summary>宿主停机时进程留在了执行宿主上：不终止、不打回队列，交给重启后的认领接回来。</summary>
    private sealed class CliRunDetachedException() : Exception("The CLI process was left running on the execution host.");

    /// <summary>
    /// 一条运行开出的用量窗口预留，以及结算时该记多少钱。
    /// </summary>
    /// <remarks>
    /// 记的是每一次进程尝试折算出的这一轮费用之和（续接被拒重试时两次相加）。进程没起来、或起来了却一个用量都没报出来，记 0：
    /// 没有任何依据去估一个金额，宁可这一轮少记，也不凭空记一笔。
    /// 可变是刻意的：它是单次执行内的记账本，只在这一个执行器实例的这一次调用里存在。
    /// </remarks>
    private sealed class UsageHold(UsageLimitReservation reservation)
    {
        public UsageLimitReservation Reservation { get; } = reservation;

        public decimal CostUsd { get; private set; }

        public void Record(decimal costUsd) => CostUsd += Math.Max(0m, costUsd);
    }
}
