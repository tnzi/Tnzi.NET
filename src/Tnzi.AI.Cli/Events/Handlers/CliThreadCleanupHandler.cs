namespace Tnzi.AI.Cli.Events.Handlers;

/// <summary>
/// 线程删除时，删掉它的外部运行留下的内容：运行记录、事件流与本机上的工作区目录，并让还没结束的运行停下来。
/// </summary>
/// <remarks>
/// <para>
/// ★ 此前线程删除只清内建 agent 的表（核心的级联清理处理器）。外部运行的提示词、交付物与整段事件流
/// （每一次工具调用的输入与输出，例如工具从用户自己的机器上取回的文件内容与命令输出）在线程删除之后照样留在库里，
/// 按运行 Id 仍然读得到；而用户以为删掉对话就删掉了这些内容。
/// </para>
/// <para>还没结束的运行分两种处置：</para>
/// <list type="bullet">
/// <item><b>排队中</b>：标记取消（认领谓词从此跳过它）后连同内容直接删掉，它一次都不会执行。</item>
/// <item><b>已认领 / 执行中</b>：请求取消（写库，本副本上的立刻中止，别的副本在下一次续租时看到），删掉已经写下的事件，
/// 清空提示词；<b>运行记录本身留给执行器</b>，它在运行结束时发现线程已删除，就把运行记录、此后写下的事件与工作区一起删掉
/// （<see cref="CliRunExecutor"/>）。这里删不得：续租看到「行没了」只会默默退出，不会去杀进程，别的副本上的进程会一直跑下去、
/// 一直往库里写事件；执行器的终态写入也找不到这一行，这条运行就既没有终态、也不会有结束事件。</item>
/// </list>
/// <para>
/// 工作区只清<b>本机</b>上的，且只清已经没有运行在用的：每线程目录在这条线程还有运行在执行时整个跳过（由执行器收尾时删），
/// 按运行分的目录逐个看。别的宿主上的副本由它们各自的回收器按 <c>Gc:CompletedTtl</c> 回收。
/// 用户提供的工作目录永远不删，只删框架在运行根下建的产物与日志目录（与回收器同一条规则）。
/// </para>
/// <para>
/// 留下的：<c>UsageLog</c> 用量行（核心处理器解除它与线程的关联，账目保留，预算与成本分析以它为准）。
/// </para>
/// <para>
/// 外部 CLI 自己的会话记录（claude 在配置目录下按会话保存的完整记录）：<see cref="CliUserConfigIsolation.ProvidedConfigDirectory"/>
/// 档下按每一轮的会话 id 在执行目标上删掉（配置目录与项目目录名再问一次启动档案，<see cref="CliLaunchProfilePurpose.SessionCleanup"/>）；
/// 执行目标连不上时只记 Warning，记录留到 CLI 自己按保留期清理。<see cref="CliUserConfigIsolation.PerWorkspaceConfigDirectory"/>
/// 档下它们随工作区一起删；其余档位的记录不在这里删，见模块文档。还在执行的运行碰过的会话由执行器收尾时删。
/// </para>
/// <para>
/// 全部按线程 / 运行 Id 谓词，重跑安全；失败让异常冒泡给事件总线（重试与死信由总线负责）。
/// 跨租户查询：处理器未必处在线程的租户里，范围由线程 Id 给定（入队时已校验线程属于派发者所在的租户）。
/// </para>
/// </remarks>
public class CliThreadCleanupHandler : IEventHandler<ThreadDeletedEvent>
{
    private static readonly JsonSerializerOptions MetadataOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IRepository<CliRun, Guid> _runRepository;
    private readonly IRepository<CliRunMessage, Guid> _messageRepository;
    private readonly CliRunCancellationRegistry _cancellationRegistry;
    private readonly CliRunSignalHub _signalHub;
    private readonly IOptionsMonitor<CliAgentOptions> _options;
    private readonly ILogger<CliThreadCleanupHandler> _logger;
    private readonly ICliHost? _host;
    private readonly ICliProviderRegistry? _providerRegistry;
    private readonly IRepository<Entities.CliRuntime, Guid>? _runtimeRepository;
    private readonly IReadOnlyList<ICliLaunchProfileContributor> _profileContributors;
    private readonly IRepository<CliRunPermissionRequest, Guid>? _permissionRepository;

    /// <summary>初始化线程删除清理处理器。</summary>
    /// <remarks>宿主、provider 表、运行时仓储缺省只为直接构造（测试）：缺了它们就找不到会话记录，不删。</remarks>
    public CliThreadCleanupHandler(
        IRepository<CliRun, Guid> runRepository,
        IRepository<CliRunMessage, Guid> messageRepository,
        CliRunCancellationRegistry cancellationRegistry,
        CliRunSignalHub signalHub,
        IOptionsMonitor<CliAgentOptions> options,
        ILogger<CliThreadCleanupHandler> logger,
        ICliHost? host = null,
        ICliProviderRegistry? providerRegistry = null,
        IRepository<Entities.CliRuntime, Guid>? runtimeRepository = null,
        IEnumerable<ICliLaunchProfileContributor>? profileContributors = null,
        IRepository<CliRunPermissionRequest, Guid>? permissionRepository = null)
    {
        _runRepository = Check.NotNull(runRepository);
        _messageRepository = Check.NotNull(messageRepository);
        _cancellationRegistry = Check.NotNull(cancellationRegistry);
        _signalHub = Check.NotNull(signalHub);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
        _host = host;
        _providerRegistry = providerRegistry;
        _runtimeRepository = runtimeRepository;
        _profileContributors = profileContributors?.ToList() ?? [];
        _permissionRepository = permissionRepository;
    }

    /// <inheritdoc />
    public async Task HandleAsync(ThreadDeletedEvent evt, CancellationToken cancellationToken = default)
    {
        Check.NotNull(evt);
        var threadId = evt.ThreadId;

        // 先于删行：会话 id 只记在运行记录上
        await RemoveSessionRecordsAsync(threadId, cancellationToken);

        var runs = await CliRunPurge.RunsOfThread(_runRepository, threadId)
            .Select(r => new { r.Id, r.Status })
            .ToListAsync(cancellationToken);

        if (runs.Count > 0)
        {
            await StopUnfinishedRunsAsync(threadId, runs.Where(r => !IsTerminal(r.Status)).Select(r => r.Id).ToList(), cancellationToken);
            await CliRunPurge.DeleteMessagesAsync(_messageRepository, runs.Select(r => r.Id).ToList(), cancellationToken);
            await CliRunPurge.DeletePermissionRequestsAsync(_permissionRepository, runs.Select(r => r.Id).ToList(), cancellationToken);

            // 状态在删除这一刻重新判定：读完列表之后才被认领的运行此刻已经在执行，它的记录留给执行器
            await _runRepository.EnsureTransactionStartedAsync(cancellationToken);
            await CliRunPurge.RunsOfThread(_runRepository, threadId)
                .Where(r => r.Status != CliRunStatus.Dispatched && r.Status != CliRunStatus.Running)
                .ExecuteDeleteAsync(cancellationToken);
        }

        var stillExecuting = await CliRunPurge.RunsOfThread(_runRepository, threadId)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        RemoveWorkspaces(threadId, runs.Select(r => r.Id).Except(stillExecuting), threadStillInUse: stillExecuting.Count > 0);

        _logger.LogInformation(
            "Removed the external runs of deleted thread {ThreadId}: {Removed} removed, {Executing} still executing and left to finish their own cleanup",
            threadId, runs.Count - stillExecuting.Count, stillExecuting.Count);
    }

    /// <summary>
    /// 删掉这条线程已经结束的运行在 CLI 配置目录里留下的会话记录（只在 <see cref="CliUserConfigIsolation.ProvidedConfigDirectory"/> 档下找得到）。
    /// </summary>
    /// <remarks>
    /// 按（运行时、租户、派出者、Agent）分组，每组问一次执行目标与启动档案（用组里最近的一轮的身份）。
    /// 执行目标连不上、文件删不掉、档案给不出配置目录时只记 Warning、不挡后面删库：用户删了对话，库里的内容不该等一个离线的容器。
    /// 读整行而不是投影：多租户关闭时 <c>TenantId</c> 不在模型里，投影它的查询翻译不了。
    /// </remarks>
    private async Task RemoveSessionRecordsAsync(Guid threadId, CancellationToken cancellationToken)
    {
        if (_host is null || _providerRegistry is null || _runtimeRepository is null)
        {
            return;
        }

        var finished = await CliRunPurge.RunsOfThread(_runRepository, threadId)
            .Where(r => r.ProviderSessionId != null && r.WorkDirectory != null
                        && (r.Status == CliRunStatus.Completed || r.Status == CliRunStatus.Failed
                            || r.Status == CliRunStatus.Cancelled || r.Status == CliRunStatus.TimedOut))
            .ToListAsync(cancellationToken);
        if (finished.Count == 0)
        {
            return;
        }

        var runtimeIds = finished.Select(r => r.CliRuntimeId).Distinct().ToList();
        var runtimes = await _runtimeRepository.AsQueryable()
            .IgnoreQueryFilters()
            .Where(r => runtimeIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, cancellationToken);

        foreach (var group in finished.GroupBy(r => (r.CliRuntimeId, r.TenantId, r.CreatorId, r.AgentId)))
        {
            if (!runtimes.TryGetValue(group.Key.CliRuntimeId, out var runtime)
                || _providerRegistry.Find(runtime.ProviderKey) is not { } provider
                || provider.UserConfigIsolation != CliUserConfigIsolation.ProvidedConfigDirectory)
            {
                continue;
            }

            var latest = group.OrderByDescending(r => r.CompletedAt).ThenByDescending(r => r.CreationTime).First();
            var scope = new CliHostScope
            {
                RunId = latest.Id,
                AgentId = latest.AgentId,
                TenantId = latest.TenantId,
                UserId = latest.CreatorId,
                ThreadId = latest.ThreadId,
                RuntimeId = latest.CliRuntimeId,
                ProviderKey = provider.Key,
                TargetId = runtime.HostId,
                RuntimeMode = runtime.Mode
            };

            try
            {
                var target = await _host.GetTargetAsync(scope, cancellationToken);
                var profile = await CliLaunchProfileResolver.ResolveAsync(target, _profileContributors, new CliLaunchProfileRequest
                {
                    Scope = scope,
                    Provider = provider,
                    WorkDirectory = latest.WorkDirectory!,
                    Purpose = CliLaunchProfilePurpose.SessionCleanup
                }, cancellationToken);

                var paths = group
                    .Select(r => r.ProviderSessionId!)
                    .Distinct(StringComparer.Ordinal)
                    .SelectMany(sessionId => CliSessionStore.Locate(provider, profile, target.FileSystem.Paths, sessionId));
                var removed = await CliSessionStore.DeleteAsync(target.FileSystem, paths, cancellationToken);

                _logger.LogInformation(
                    "Removed {Count} session record(s) of deleted thread {ThreadId} on {Host}", removed, threadId, target.Name);
            }
            catch (Exception ex) when (ex is CliHostUnavailableException or IOException or UnauthorizedAccessException
                                           or CliProviderConfigurationException)
            {
                _logger.LogWarning(ex,
                    "Could not remove the session records of deleted thread {ThreadId} (provider {Provider}); the CLI's own retention removes them later",
                    threadId, provider.Key);
            }
        }
    }

    /// <summary>
    /// 让这条线程还没结束的运行停下来，并清空它们的提示词。
    /// </summary>
    /// <remarks>
    /// 取消写库是跨副本的权威事实：排队中的从此不会被认领（认领谓词要求没有取消请求），执行中的由它的宿主在续租时看到并整树终止进程；
    /// 本副本上正在跑的直接经登记处中止，不必等下一个续租周期。提示词清空是为宿主崩溃那一种情形：执行器正常结束时会删掉整条记录，
    /// 宿主崩溃时租约回收只把它判成 <c>Cancelled</c>，不清空的话提示词就跟着那条记录一直留着。
    /// </remarks>
    private async Task StopUnfinishedRunsAsync(Guid threadId, IReadOnlyCollection<Guid> unfinished, CancellationToken cancellationToken)
    {
        if (unfinished.Count == 0)
        {
            return;
        }

        await _runRepository.EnsureTransactionStartedAsync(cancellationToken);
        await CliRunPurge.RunsOfThread(_runRepository, threadId)
            .Where(r => r.Status == CliRunStatus.Queued || r.Status == CliRunStatus.Dispatched || r.Status == CliRunStatus.Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.CancelRequested, true)
                .SetProperty(r => r.Prompt, string.Empty)
                .SetProperty(r => r.PerTurnContext, (string?)null), cancellationToken);

        foreach (var runId in unfinished)
        {
            _cancellationRegistry.TryCancel(runId);
            _signalHub.Signal(runId);
        }
    }

    /// <summary>
    /// 删掉本机上属于这条线程、已经没有运行在用的工作区目录。
    /// </summary>
    /// <param name="threadId">线程：每线程目录以它命名。</param>
    /// <param name="finishedRunIds">已经删掉记录的运行：按运行分的目录（每运行模式、用户目录模式）以它们命名。</param>
    /// <param name="threadStillInUse">这条线程还有运行在执行：每线程目录此刻正被它用着，留给执行器收尾时删。</param>
    private void RemoveWorkspaces(Guid threadId, IEnumerable<Guid> finishedRunIds, bool threadStillInUse)
    {
        var root = CliWorkspaceLayout.ResolveWorkspacesRoot(_options.CurrentValue);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return;
        }

        var owners = finishedRunIds.Select(id => id.ToString("N")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!threadStillInUse)
        {
            owners.Add(threadId.ToString("N"));
        }

        foreach (var runRoot in CliWorkspaceLayout.EnumerateRunRoots(root).Where(d => owners.Contains(Path.GetFileName(d))))
        {
            RemoveRunRoot(runRoot);
            _logger.LogInformation("Removed workspace {Directory} of deleted thread {ThreadId}", runRoot, threadId);
        }
    }

    /// <summary>删一个运行根目录；工作目录属于用户时只删框架自己建的产物与日志目录。</summary>
    private static void RemoveRunRoot(string runRoot)
    {
        if (ReadGcMetadata(runRoot)?.UserOwnedWorkDirectory == true)
        {
            DeleteDirectory(Path.Combine(runRoot, CliWorkspaceLayout.OutputDirectoryName));
            DeleteDirectory(Path.Combine(runRoot, CliWorkspaceLayout.LogDirectoryName));
            DeleteDirectory(Path.Combine(runRoot, CliWorkspaceLayout.MetadataDirectoryName));
            return;
        }

        DeleteDirectory(runRoot);
    }

    /// <summary>读回收元数据；不存在或读不了时为 null（按受管目录处理：它是以我们的 Id 命名的目录）。</summary>
    private static CliWorkspaceGcMetadata? ReadGcMetadata(string runRoot)
    {
        var path = Path.Combine(runRoot, CliWorkspaceLayout.GcMetadataFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CliWorkspaceGcMetadata>(File.ReadAllText(path), MetadataOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>目录不在就算完成（重跑、与回收器赛跑都安全）；真实的 IO 失败冒泡给事件总线。</summary>
    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (DirectoryNotFoundException)
        {
            // 与回收器同时删到了同一个目录
        }
    }

    private static bool IsTerminal(CliRunStatus status)
        => status is CliRunStatus.Completed or CliRunStatus.Failed or CliRunStatus.Cancelled or CliRunStatus.TimedOut;
}
