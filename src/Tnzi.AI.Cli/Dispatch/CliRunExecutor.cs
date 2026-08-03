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
    private static readonly TimeSpan WatchdogTick = TimeSpan.FromSeconds(5);

    private readonly IRepository<CliRun, Guid> _runRepository;
    private readonly IRepository<CliRunMessage, Guid> _messageRepository;
    private readonly IRepository<CliAgentBinding, Guid> _bindingRepository;
    private readonly IRepository<Entities.CliRuntime, Guid> _runtimeRepository;
    private readonly IRepository<Agent, Guid> _agentRepository;
    private readonly ICliProviderRegistry _providerRegistry;
    private readonly ICliProtocolAdapterFactory _adapterFactory;
    private readonly ICliProcessHost _processHost;
    private readonly ICliWorkspacePreparer _workspacePreparer;
    private readonly ICliBriefComposer _briefComposer;
    private readonly ICliMcpConfigComposer _mcpConfigComposer;
    private readonly CliRunTokenService _tokenService;
    private readonly ICliExecutableResolver _executableResolver;
    private readonly IAgentGrantService _grantService;
    private readonly ISkillService _skillService;
    private readonly CliRunSignalHub _signalHub;
    private readonly ICostCalculator? _costCalculator;
    private readonly IUsageLogService? _usageLogService;
    private readonly IBudgetService? _budgetService;
    private readonly IOptionsMonitor<CliAgentOptions> _options;
    private readonly ILogger<CliRunExecutor> _logger;

    /// <summary>初始化运行执行器。</summary>
    public CliRunExecutor(
        IRepository<CliRun, Guid> runRepository,
        IRepository<CliRunMessage, Guid> messageRepository,
        IRepository<CliAgentBinding, Guid> bindingRepository,
        IRepository<Entities.CliRuntime, Guid> runtimeRepository,
        IRepository<Agent, Guid> agentRepository,
        ICliProviderRegistry providerRegistry,
        ICliProtocolAdapterFactory adapterFactory,
        ICliProcessHost processHost,
        ICliWorkspacePreparer workspacePreparer,
        ICliBriefComposer briefComposer,
        ICliMcpConfigComposer mcpConfigComposer,
        CliRunTokenService tokenService,
        ICliExecutableResolver executableResolver,
        IAgentGrantService grantService,
        ISkillService skillService,
        CliRunSignalHub signalHub,
        IOptionsMonitor<CliAgentOptions> options,
        ILogger<CliRunExecutor> logger,
        ICostCalculator? costCalculator = null,
        IUsageLogService? usageLogService = null,
        IBudgetService? budgetService = null)
    {
        _runRepository = Check.NotNull(runRepository);
        _messageRepository = Check.NotNull(messageRepository);
        _bindingRepository = Check.NotNull(bindingRepository);
        _runtimeRepository = Check.NotNull(runtimeRepository);
        _agentRepository = Check.NotNull(agentRepository);
        _providerRegistry = Check.NotNull(providerRegistry);
        _adapterFactory = Check.NotNull(adapterFactory);
        _processHost = Check.NotNull(processHost);
        _workspacePreparer = Check.NotNull(workspacePreparer);
        _briefComposer = Check.NotNull(briefComposer);
        _mcpConfigComposer = Check.NotNull(mcpConfigComposer);
        _tokenService = Check.NotNull(tokenService);
        _executableResolver = Check.NotNull(executableResolver);
        _grantService = Check.NotNull(grantService);
        _skillService = Check.NotNull(skillService);
        _signalHub = Check.NotNull(signalHub);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
        _costCalculator = costCalculator;
        _usageLogService = usageLogService;
        _budgetService = budgetService;
    }

    /// <summary>执行一条运行，直到终态。</summary>
    public async Task ExecuteAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await _runRepository.AsQueryable(withTracking: true).FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
        {
            _logger.LogWarning("Claimed CLI run {RunId} no longer exists", runId);
            return;
        }

        CliWorkspace? workspace = null;
        var options = _options.CurrentValue;

        try
        {
            // 预算门排在 setup 解析之前：注定被拒的运行不该先跑掉 3~4 次查询去解析绑定与运行时。
            if (!await EnsureBudgetAsync(run, cancellationToken))
            {
                return;
            }

            var setup = await ResolveSetupAsync(run, cancellationToken);
            if (setup is null)
            {
                return;
            }

            workspace = await PrepareWorkspaceAsync(run, setup, options, cancellationToken);

            run.Status = CliRunStatus.Running;
            run.StartedAt = DateTime.UtcNow;
            run.WorkDirectory = workspace.WorkDirectory;
            await _runRepository.UpdateAsync(run, cancellationToken);
            await _runRepository.SaveChangesAsync(cancellationToken);
            _signalHub.Signal(runId);

            var result = await RunProcessAsync(run, setup, workspace, options, cancellationToken);

            // 续接被拒:会话指针失效(CLI 侧归档过期、换了机器、目录被回收)。
            // 一次重试**开新会话**而不是把错误抛给用户 —— 用户问的是一个问题,
            // 不是要来管理会话指针的。上下文确实丢了,所以 PerTurnContext 里会明说,
            // 否则 agent 会自然地假装连续("如我之前所说…")而用户无从察觉。
            if (result.ResumeRejected && run.ProviderSessionId is null && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "CLI run {RunId} had its resume rejected; retrying once with a fresh session", run.Id);

                run.ResumeExpected = false;
                run.PerTurnContext = AppendContextLossNotice(run.PerTurnContext);
                await RunProcessAsync(run, setup, workspace, options, cancellationToken);
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
        catch (CliProtocolNotImplementedException ex)
        {
            await FailAsync(run, CliRunFailureReason.LaunchFailed, ex.Message, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            await FailAsync(run, CliRunFailureReason.WorkspacePrepareFailed, ex.Message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 宿主停机：把运行打回队列而不是判失败 —— 它一次都没跑完，别的副本还能接。
            await ReleaseToQueueAsync(run);
        }
        finally
        {
            if (workspace is not null)
            {
                await CleanupWorkspaceAsync(workspace);
            }

            _signalHub.Signal(runId);
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

    private async Task<CliRunSetup?> ResolveSetupAsync(CliRun run, CancellationToken cancellationToken)
    {
        var binding = await _bindingRepository.FirstOrDefaultAsync(b => b.AgentId == run.AgentId, cancellationToken);
        if (binding is null)
        {
            await FailAsync(run, CliRunFailureReason.Unknown,
                "The agent no longer has an external CLI binding.", cancellationToken);
            return null;
        }

        var runtime = await _runtimeRepository.GetAsync(binding.CliRuntimeId, cancellationToken);
        if (runtime is null)
        {
            await FailAsync(run, CliRunFailureReason.Unknown,
                "The bound external CLI runtime no longer exists.", cancellationToken);
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

        // 优先用注册时探测到的路径；它不在了（CLI 被卸载/升级换路径）就现场重新解析。
        var executablePath = File.Exists(runtime.ExecutablePath)
            ? runtime.ExecutablePath
            : _executableResolver.Resolve(provider);

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            await FailAsync(run, CliRunFailureReason.ExecutableNotFound,
                $"Executable for provider '{provider.Key}' was not found on host '{runtime.HostId}'.", cancellationToken);
            return null;
        }

        var agent = await _agentRepository.GetAsync(run.AgentId, cancellationToken);
        if (agent is null)
        {
            await FailAsync(run, CliRunFailureReason.Unknown, "The agent no longer exists.", cancellationToken);
            return null;
        }

        return new CliRunSetup(binding, runtime, provider, executablePath, agent);
    }

    private async Task<CliWorkspace> PrepareWorkspaceAsync(
        CliRun run, CliRunSetup setup, CliAgentOptions options, CancellationToken cancellationToken)
    {
        var skills = setup.Binding.MaterializeSkills
            ? await LoadSkillsAsync(run.AgentId, cancellationToken)
            : [];

        // 凭据必须在布置工作区之前签发：MCP 配置文件是工作区的一部分，由布置器写出。
        var writeBackToken = options.WriteBack.Enabled
            ? await _tokenService.IssueAsync(run, options.WriteBack.TokenLifetime, cancellationToken)
            : null;

        var context = new CliRunContext
        {
            RunId = run.Id,
            AgentId = run.AgentId,
            TenantId = run.TenantId,
            ThreadId = run.ThreadId,
            Provider = setup.Provider,
            StableBrief = setup.Binding.InjectAgentInstructions
                ? _briefComposer.Compose(setup.Agent, setup.Provider)
                : string.Empty,
            PerTurnContext = run.PerTurnContext,
            WorkDirectoryMode = setup.Binding.WorkDirectoryMode,
            UserWorkDirectory = setup.Binding.UserWorkDirectory,
            Skills = skills,
            McpConfigJson = _mcpConfigComposer.Compose(
                setup.Binding.McpConfigJson, writeBackToken, options.WriteBack)
        };

        // 续接一个还在原地的工作目录，比重新布置更接近 agent 上一轮结束时的状态
        //（它可能已经 clone 了仓库、装了依赖）。
        if (!string.IsNullOrWhiteSpace(run.WorkDirectory))
        {
            var reused = await _workspacePreparer.ReuseAsync(run.WorkDirectory, context, cancellationToken);
            if (reused is not null)
            {
                return reused;
            }
        }

        return await _workspacePreparer.PrepareAsync(context, cancellationToken);
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

    private async Task<CliAgentResult> RunProcessAsync(
        CliRun run, CliRunSetup setup, CliWorkspace workspace, CliAgentOptions options, CancellationToken cancellationToken)
    {
        var adapter = _adapterFactory.Create(setup.Provider.Protocol);
        var launchContext = BuildLaunchContext(run, setup, workspace, options);
        var spec = adapter.BuildProcess(launchContext);

        var idle = ResolveIdleWatchdog(setup.Binding, options);
        using var watchdog = new CliRunWatchdog(idle, options.ToolWatchdog, options.HardTimeout, cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        await using var process = await _processHost.StartAsync(spec, cancellationToken);

        var sequence = run.MessageCount;
        var sessionId = run.ProviderSessionId;
        var externallyCancelled = false;

        using var ticker = new Timer(_ => watchdog.CheckAndTrip(), null, WatchdogTick, WatchdogTick);

        try
        {
            await foreach (var evt in adapter.RunAsync(process.Transport, launchContext, watchdog.Token))
            {
                watchdog.Observe(evt);

                if (!string.IsNullOrWhiteSpace(evt.SessionId))
                {
                    sessionId = evt.SessionId;
                }

                sequence++;
                await PersistEventAsync(run, evt, sequence, cancellationToken);
                _signalHub.Signal(run.Id);
            }
        }
        catch (OperationCanceledException)
        {
            // 三种来源：用户取消、宿主停机、看门狗。用 watchdog.Failure 区分 ——
            // 它是唯一在做出判断的那一刻记下原因的地方。
            externallyCancelled = watchdog.Failure is null;
        }
        finally
        {
            stopwatch.Stop();
            await process.TerminateAsync(CancellationToken.None);
        }

        var outcome = new CliSessionOutcome
        {
            ExitCode = process.ExitCode,
            StderrTail = process.Transport.StderrTail,
            Elapsed = stopwatch.Elapsed,
            Cancelled = externallyCancelled,
            WatchdogFailure = watchdog.Failure
        };

        var result = adapter.GetResult(outcome);
        await CompleteAsync(run, setup, result, sessionId, sequence, cancellationToken);
        return result;
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
        return string.IsNullOrWhiteSpace(perTurnContext)
            ? notice
            : perTurnContext + Environment.NewLine + Environment.NewLine + notice;
    }

    private CliAgentLaunchContext BuildLaunchContext(
        CliRun run, CliRunSetup setup, CliWorkspace workspace, CliAgentOptions options)
    {
        var customArgs = ParseArgs(setup.Binding.CustomArgsJson);

        // 每轮易变的上下文追加到<b>提示尾部</b>，绝不进 brief：brief 在缓存前缀里，
        // 一变就作废整段历史的 prompt cache。
        var prompt = string.IsNullOrWhiteSpace(run.PerTurnContext)
            ? run.Prompt
            : $"{run.Prompt}\n\n---\n\n{run.PerTurnContext}";

        return new CliAgentLaunchContext
        {
            Provider = setup.Provider,
            ExecutablePath = setup.ExecutablePath,
            Prompt = prompt,
            WorkingDirectory = workspace.WorkDirectory,
            Model = string.IsNullOrWhiteSpace(setup.Binding.Model) ? setup.Provider.DefaultModel : setup.Binding.Model,
            ThinkingLevel = setup.Binding.ThinkingLevel,
            ResumeSessionId = run.ProviderSessionId,
            ResumeExpected = run.ResumeExpected,
            InlineSystemPrompt = setup.Provider.RequiresInlineSystemPrompt && setup.Binding.InjectAgentInstructions
                ? _briefComposer.Compose(setup.Agent, setup.Provider)
                : null,
            McpConfigPath = workspace.McpConfigPath,
            ExtraArgs = setup.Provider.ExtraArgs,
            CustomArgs = customArgs,
            InheritAllHostEnvironment = options.InheritAllHostEnvironment,
            EnvironmentWhitelist = options.EnvironmentWhitelist,
            HandshakeTimeout = options.HandshakeTimeout,
            TerminateGrace = options.TerminateGrace
        };
    }

    /// <summary>
    /// 解析生效的空闲阈值。每 agent 的覆盖<b>只允许收紧</b>：
    /// 允许放宽等于让任意一个 agent 自己解除全局安全边界。
    /// </summary>
    private static TimeSpan ResolveIdleWatchdog(CliAgentBinding binding, CliAgentOptions options)
        => binding.IdleWatchdog is { } perAgent && perAgent > TimeSpan.Zero && perAgent < options.IdleWatchdog
            ? perAgent
            : options.IdleWatchdog;

    private async Task PersistEventAsync(CliRun run, CliAgentEvent evt, int sequence, CancellationToken cancellationToken)
    {
        var message = new CliRunMessage
        {
            TenantId = run.TenantId,
            RunId = run.Id,
            Sequence = sequence,
            Type = evt.Type,
            Content = evt.Content,
            Tool = evt.Tool,
            CallId = evt.CallId,
            InputJson = evt.Input is null ? null : JsonSerializer.Serialize(evt.Input, TnziJsonDefaults.Options),
            Output = Truncate(evt.Output, ToolOutputMaxLength),
            Status = evt.Status,
            Level = evt.Level
        };

        await _messageRepository.InsertAsync(message, cancellationToken);
        await _messageRepository.SaveChangesAsync(cancellationToken);
    }

    private async Task CompleteAsync(
        CliRun run, CliRunSetup setup, CliAgentResult result, string? sessionId, int sequence,
        CancellationToken cancellationToken)
    {
        run.Status = result.Status;
        run.FailureReason = result.FailureReason;
        run.Output = result.Output;
        run.Error = result.Error;
        run.DurationMs = result.DurationMs;
        run.CompletedAt = DateTime.UtcNow;
        run.MessageCount = sequence;
        run.LeaseExpiresAt = null;

        // resume 被拒时适配器已经把 SessionId 清空了；这里保持它的判断，
        // 不要拿流里看到的旧 id 覆盖回去 —— 那个 id 已经确定用不了了。
        run.ProviderSessionId = result.ResumeRejected ? null : result.SessionId ?? sessionId;

        if (result.Usage.Count > 0)
        {
            run.UsageJson = JsonSerializer.Serialize(result.Usage, TnziJsonDefaults.Options);
            run.EstimatedCostUsd = ResolveCost(setup, result);
        }

        // 运行到达终态：回写凭据立刻作废。ValidateAsync 已按状态挡掉了，
        // 但清掉哈希意味着终态记录里根本不留任何凭据材料。
        run.WriteBackTokenHash = null;
        run.WriteBackTokenExpiresAt = null;

        await _runRepository.UpdateAsync(run, cancellationToken);
        await _runRepository.SaveChangesAsync(cancellationToken);

        await LogUsageAsync(run, setup, result, cancellationToken);

        _logger.LogInformation(
            "CLI run {RunId} finished with status {Status} in {DurationMs}ms ({Provider})",
            run.Id, run.Status, run.DurationMs, setup.Provider.Key);
    }

    /// <summary>
    /// 结算成本：provider 自报值优先，没有才回落到按 token 估算。
    /// </summary>
    /// <remarks>
    /// 自报值优先不是偏好问题：按 token 单价估算复现不了请求级定价规则
    /// （某些厂商在 prompt 超阈值后整请求翻倍），而一条用量记录聚合了一个 turn 里的多次调用，
    /// 事后无法判断哪一次踩了哪一档。
    /// </remarks>
    private decimal? ResolveCost(CliRunSetup setup, CliAgentResult result)
    {
        var reported = result.Usage.Values
            .Where(u => u.ReportedCostUsd.HasValue)
            .Sum(u => u.ReportedCostUsd!.Value);

        if (reported > 0m)
        {
            return reported;
        }

        if (_costCalculator is null)
        {
            return null;
        }

        decimal total = 0m;
        var any = false;
        foreach (var (model, usage) in result.Usage)
        {
            var cost = _costCalculator.CalculateCost(
                setup.Provider.Key, model, (int)usage.InputTokens, (int)usage.OutputTokens);
            if (cost.HasValue)
            {
                total += cost.Value;
                any = true;
            }
        }

        return any ? total : null;
    }

    private async Task LogUsageAsync(
        CliRun run, CliRunSetup setup, CliAgentResult result, CancellationToken cancellationToken)
    {
        if (_usageLogService is null || result.Usage.Count == 0)
        {
            return;
        }

        foreach (var (model, usage) in result.Usage)
        {
            await _usageLogService.LogUsageAsync(
                AIOperationType.AgentRun,
                setup.Provider.Key,
                model,
                (int)usage.InputTokens,
                (int)usage.OutputTokens,
                run.DurationMs,
                run.Status == CliRunStatus.Completed,
                run.Error,
                run.AgentId,
                run.ThreadId,
                run.EstimatedCostUsd,
                cancellationToken);
        }
    }

    private async Task FailAsync(
        CliRun run, CliRunFailureReason reason, string message, CancellationToken cancellationToken)
    {
        run.Status = CliRunStatus.Failed;
        run.FailureReason = reason;
        run.Error = message;
        run.CompletedAt = DateTime.UtcNow;
        run.LeaseExpiresAt = null;
        run.WriteBackTokenHash = null;
        run.WriteBackTokenExpiresAt = null;

        await _runRepository.UpdateAsync(run, cancellationToken);
        await _runRepository.SaveChangesAsync(cancellationToken);

        _logger.LogWarning("CLI run {RunId} failed ({Reason}): {Message}", run.Id, reason, message);
    }

    private async Task ReleaseToQueueAsync(CliRun run)
    {
        run.Status = CliRunStatus.Queued;
        run.LeaseExpiresAt = null;
        run.ClaimedByHostId = null;
        run.DispatchedAt = null;

        // 用 None：宿主已经在停机，带着已取消的令牌去写库只会让这条运行卡在 Dispatched。
        await _runRepository.UpdateAsync(run, CancellationToken.None);
        await _runRepository.SaveChangesAsync(CancellationToken.None);

        _logger.LogInformation("CLI run {RunId} released back to the queue during host shutdown", run.Id);
    }

    private async Task CleanupWorkspaceAsync(CliWorkspace workspace)
    {
        try
        {
            // 只回滚受管写入，不删整个目录：终态目录由 GC 按 TTL 回收，
            // 立刻删掉会让排障时连产物和日志都看不到。
            await _workspacePreparer.CleanupAsync(workspace, removeAll: false, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not clean up workspace at {Root}", workspace.RootDirectory);
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

    private static string? Truncate(string? value, int maxLength)
        => value is not null && value.Length > maxLength
            ? value[..maxLength] + $"\n… [truncated, {value.Length - maxLength} more characters]"
            : value;

    private sealed record CliRunSetup(
        CliAgentBinding Binding,
        Entities.CliRuntime Runtime,
        CliProviderDescriptor Provider,
        string ExecutablePath,
        Agent Agent);
}
