namespace Tnzi.AI.Cli.Dispatch;

/// <summary>
/// 一次进程（一次尝试）的事件序号。主循环写适配器的事件、批准通道写权限结局，两边都从这里取号、在闸门里落库。
/// </summary>
internal sealed class CliEventSequence(int start) : IDisposable
{
    /// <summary>取号与落库的闸门：号码的次序就是落库的次序。</summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);

    /// <summary>最后一个已分配的序号。只在持有 <see cref="Gate"/> 时读写。</summary>
    public int Current { get; set; } = start;

    public void Dispose() => Gate.Dispose();
}

/// <summary>
/// 「先问我」档位在有人值守的一轮上的批准通道：适配器交出的权限请求落成一行 <see cref="CliRunPermissionRequest"/>，
/// 使用方经回答接口写下回答（任何一个服务端副本），这里轮询到之后写给 CLI、在事件流里记下结局。
/// </summary>
/// <remarks>
/// <para>
/// <b>结局写出的次序</b>：先在请求行上记下这一刻的事件序号（<see cref="CliRunPermissionRequest.ResolvedSequence"/>），再落结局事件，
/// 最后才把回答写到 CLI 的 stdin。CLI 因这个回答而写出的任何东西因此一定排在结局事件之后。
/// </para>
/// <para>
/// <b>重新附着</b>：重放从进程第一行输出起，适配器再一次交出同样的请求（同样的序号），这里按 (运行, 序号) 认回原来那一行。
/// 停机前已经写给 CLI 的回答在重放到它的 <see cref="CliRunPermissionRequest.ResolvedSequence"/> 那一刻原样再交给适配器一次：
/// 那一行 stdin 进程早已收到，重放的 transport 按行数丢掉它，但计数与事件序号都因此与停机前逐个对得上，
/// 依赖这个回答的输出行也等得到它。停机时还在等的请求重新挂上（期限从请求出现时算起），重放追上已落库的事件之后照常等回答、照常写出。
/// </para>
/// <para>
/// 等回答期间空闲与工具看门狗暂停（<see cref="CliRunWatchdog.BeginApprovalWait"/>），超时由这里按 <c>AI:Cli:Approval:Timeout</c> 判。
/// </para>
/// </remarks>
internal sealed class CliRunApprovals
{
    private const string TimedOutMessage = "No one answered this permission request in time.";
    private const string EndedMessage = "The run ended before this permission request was answered.";

    private readonly CliRun _run;
    private readonly ICliProtocolAdapter _adapter;
    private readonly CliEventSequence _sequence;
    private readonly CliRunWatchdog _watchdog;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CliRunSignalHub _signalHub;
    private readonly CliApprovalOptions _options;
    private readonly int _persistedThrough;
    private readonly Func<CliAgentEvent, int, Task> _persist;
    private readonly ILogger _logger;

    /// <summary>这一次尝试里出现过的请求，按请求 id。只在持有序号闸门时改。</summary>
    private readonly Dictionary<Guid, Tracked> _requests = [];

    /// <summary>重放中要在某个序号上原样再交给适配器的结局（停机前已写给 CLI 的）。</summary>
    private readonly Dictionary<int, Tracked> _recorded = [];

    /// <param name="run">运行。</param>
    /// <param name="adapter">这一次尝试的适配器。</param>
    /// <param name="sequence">与主循环共用的序号。</param>
    /// <param name="watchdog">这一次尝试的看门狗。</param>
    /// <param name="scopeFactory">每次读写库开一个作用域（同事件落库）。</param>
    /// <param name="signalHub">回答落在本副本时的提示。</param>
    /// <param name="options">超时与轮询间隔。</param>
    /// <param name="persistedThrough">重放时已落库的最后一个事件序号；新进程为 <see cref="int.MinValue"/>。</param>
    /// <param name="persist">把一条事件按序号落库（执行器的那一份，含工具结果的处理）。</param>
    /// <param name="logger">日志。</param>
    public CliRunApprovals(
        CliRun run, ICliProtocolAdapter adapter, CliEventSequence sequence, CliRunWatchdog watchdog, IServiceScopeFactory scopeFactory,
        CliRunSignalHub signalHub, CliApprovalOptions options, int persistedThrough, Func<CliAgentEvent, int, Task> persist, ILogger logger)
    {
        _run = Check.NotNull(run);
        _adapter = Check.NotNull(adapter);
        _sequence = Check.NotNull(sequence);
        _watchdog = Check.NotNull(watchdog);
        _scopeFactory = Check.NotNull(scopeFactory);
        _signalHub = Check.NotNull(signalHub);
        _options = Check.NotNull(options);
        _persistedThrough = persistedThrough;
        _persist = Check.NotNull(persist);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 主循环拿到一个权限请求事件、分到序号 <paramref name="sequence"/> 时调用（持有序号闸门）：建出（或重放时认回）请求行，
    /// 返回带上请求 id 与期限的事件。
    /// </summary>
    public async Task<CliAgentEvent> OpenAsync(CliAgentEvent evt, int sequence)
    {
        var permission = evt.Permission ?? throw new InvalidOperationException("A permission request event carries no permission.");
        var key = permission.ProtocolKey ?? throw new InvalidOperationException("A permission request event carries no protocol key.");

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<CliRunPermissionRequest, Guid>>();

        var row = await repository.AsQueryable().IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(r => r.RunId == _run.Id && r.Sequence == sequence, CancellationToken.None);
        if (row is null)
        {
            row = new CliRunPermissionRequest
            {
                Id = SequentialGuid.NewGuid(),
                TenantId = _run.TenantId,
                RunId = _run.Id,
                Sequence = sequence,
                ProtocolKey = key,
                Tool = CliRunExecutor.Truncate(evt.Tool, 200),
                OptionsJson = JsonSerializer.Serialize(permission.Options, TnziJsonDefaults.Options),
                Status = CliPermissionStatus.Pending,
                ExpiresAt = DateTime.UtcNow + _options.Timeout
            };
            await repository.InsertAsync(row, CancellationToken.None);
            await repository.SaveChangesAsync(CancellationToken.None);
        }

        var tracked = new Tracked(row.Id, key, evt.Tool, evt.CallId);
        _requests[row.Id] = tracked;

        if (row.ResolvedSequence is { } resolvedAt)
        {
            // 停机前已经写给 CLI：重放到那个序号时原样再交一次（见类说明）
            tracked.Resolution = Resolution.From(row);
            _recorded[resolvedAt] = tracked;
        }
        else
        {
            _watchdog.BeginApprovalWait();
            tracked.Waiting = true;
        }

        return evt with { Permission = permission with { RequestId = row.Id, ExpiresAt = row.ExpiresAt } };
    }

    /// <summary>
    /// 主循环每处理完一条事件调用（持有序号闸门）：下一个序号若是重放中停机前写出的某个结局，就在这里把它原样交给适配器、占掉那个序号。
    /// </summary>
    /// <remarks>
    /// 必须紧跟在前一条事件之后做，而不是等下一条事件到了再做：依赖这个回答的输出行要等适配器写出它才会交出来，
    /// 等它到了再做就是互相等，直到重放的次序等待到期。
    /// </remarks>
    public async Task ReplayRecordedAsync()
    {
        while (_recorded.Remove(_sequence.Current + 1, out var tracked))
        {
            var slot = ++_sequence.Current;

            // 停机发生在记下序号与落结局事件之间：那条事件现在补上
            if (slot > _persistedThrough)
            {
                await _persist(ResolutionEvent(tracked, tracked.Resolution!), slot);
            }

            await _adapter.AnswerPermissionAsync(tracked.ProtocolKey, tracked.Resolution!.ToDecision(), CancellationToken.None);
            tracked.Delivered = true;
        }
    }

    /// <summary>
    /// 后台循环：查还在等的请求有没有回答、过没过期，有了结局就写出。随这一次尝试结束（<paramref name="cancellationToken"/>）。
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await DeliverReadyAsync(cancellationToken);
                await _signalHub.WaitAsync(_run.Id, _options.PollInterval, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // 一次查库失败不能让还在等的请求永远没有结局：下一轮再试
                _logger.LogWarning(ex, "Could not check the permission requests of CLI run {RunId}; retrying", _run.Id);
                await Task.Delay(_options.PollInterval, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    /// <summary>
    /// 这一次尝试结束（进程已经不在，或这条运行被打回队列）：还没写给 CLI 的请求一律以 <see cref="CliPermissionStatus.Cancelled"/> 结束并记下结局事件。
    /// 进程留在宿主上等重新附着时不调用。
    /// </summary>
    public async Task CloseAsync()
    {
        await _sequence.Gate.WaitAsync(CancellationToken.None);
        try
        {
            foreach (var tracked in _requests.Values.Where(t => !t.Delivered).ToList())
            {
                using var scope = _scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IRepository<CliRunPermissionRequest, Guid>>();
                var now = DateTime.UtcNow;

                // 已经有了回答、只是还没来得及写给 CLI 的也算随运行结束：那个回答没有生效
                await repository.EnsureTransactionStartedAsync(CancellationToken.None);
                await repository.AsQueryable().IgnoreQueryFilters()
                    .Where(r => r.Id == tracked.RequestId && r.ResolvedSequence == null)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.Status, CliPermissionStatus.Cancelled)
                        .SetProperty(r => r.AnsweredAt, r => r.AnsweredAt ?? now), CancellationToken.None);

                var row = await FindAsync(repository, tracked.RequestId);
                if (row is null || row.ResolvedSequence is not null)
                {
                    continue;
                }

                var slot = ++_sequence.Current;
                await RecordResolvedSequenceAsync(repository, tracked.RequestId, slot);
                await _persist(ResolutionEvent(tracked, Resolution.From(row)), slot);
                tracked.Delivered = true;
                EndWait(tracked);
            }
        }
        finally
        {
            _sequence.Gate.Release();
        }

        _signalHub.Signal(_run.Id);
    }

    private async Task DeliverReadyAsync(CancellationToken cancellationToken)
    {
        List<Guid> waiting;
        await _sequence.Gate.WaitAsync(cancellationToken);
        try
        {
            // 重放还没追上停机前落库的事件：这时写出新结局会占掉别的事件的序号
            if (_sequence.Current < _persistedThrough)
            {
                return;
            }

            waiting = _requests.Values.Where(t => !t.Delivered).Select(t => t.RequestId).ToList();
        }
        finally
        {
            _sequence.Gate.Release();
        }

        if (waiting.Count == 0)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<CliRunPermissionRequest, Guid>>();

        // 过期的先按条件改成超时：与回答赛跑时先写下的为准
        var now = DateTime.UtcNow;
        await repository.EnsureTransactionStartedAsync(cancellationToken);
        await repository.AsQueryable().IgnoreQueryFilters()
            .Where(r => waiting.Contains(r.Id) && r.Status == CliPermissionStatus.Pending && r.ExpiresAt <= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, CliPermissionStatus.TimedOut)
                .SetProperty(r => r.AnsweredAt, (DateTime?)now), cancellationToken);

        var resolved = await repository.AsQueryable().IgnoreQueryFilters().AsNoTracking()
            .Where(r => waiting.Contains(r.Id) && r.Status != CliPermissionStatus.Pending)
            .OrderBy(r => r.Sequence)
            .ToListAsync(cancellationToken);

        foreach (var row in resolved)
        {
            await DeliverAsync(repository, row);
        }
    }

    private async Task DeliverAsync(IRepository<CliRunPermissionRequest, Guid> repository, CliRunPermissionRequest row)
    {
        Tracked? tracked;
        var resolution = Resolution.From(row);

        await _sequence.Gate.WaitAsync(CancellationToken.None);
        try
        {
            if (!_requests.TryGetValue(row.Id, out tracked) || tracked.Delivered)
            {
                return;
            }

            // 先记序号、再落事件、最后写 stdin（见类说明）
            var slot = ++_sequence.Current;
            await RecordResolvedSequenceAsync(repository, row.Id, slot);
            await _persist(ResolutionEvent(tracked, resolution), slot);
            tracked.Delivered = true;
        }
        finally
        {
            _sequence.Gate.Release();
        }

        _signalHub.Signal(_run.Id);

        try
        {
            if (!await _adapter.AnswerPermissionAsync(tracked.ProtocolKey, resolution.ToDecision(), CancellationToken.None))
            {
                _logger.LogWarning(
                    "CLI run {RunId}: the adapter no longer knew permission request {RequestId}; its answer was not written", _run.Id, row.Id);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // 进程刚好退出：结局已经记下，没有人再等这个回答
            _logger.LogWarning(ex, "CLI run {RunId}: could not write the answer to permission request {RequestId}", _run.Id, row.Id);
        }
        finally
        {
            EndWait(tracked);
        }
    }

    private void EndWait(Tracked tracked)
    {
        if (tracked.Waiting)
        {
            tracked.Waiting = false;
            _watchdog.EndApprovalWait();
        }
    }

    private static Task<CliRunPermissionRequest?> FindAsync(IRepository<CliRunPermissionRequest, Guid> repository, Guid id)
        => repository.AsQueryable().IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, CancellationToken.None);

    private static async Task RecordResolvedSequenceAsync(IRepository<CliRunPermissionRequest, Guid> repository, Guid id, int slot)
    {
        await repository.EnsureTransactionStartedAsync(CancellationToken.None);
        await repository.AsQueryable().IgnoreQueryFilters()
            .Where(r => r.Id == id && r.ResolvedSequence == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ResolvedSequence, (int?)slot), CancellationToken.None);
    }

    private static CliAgentEvent ResolutionEvent(Tracked tracked, Resolution resolution) => new()
    {
        Type = CliAgentEventType.PermissionResolved,
        Tool = tracked.Tool,
        CallId = tracked.CallId,
        Permission = new CliAgentPermission
        {
            RequestId = tracked.RequestId,
            Status = resolution.Status,
            OptionId = resolution.OptionId,
            AnsweredBy = resolution.AnsweredBy
        }
    };

    /// <summary>这一次尝试里的一个请求。</summary>
    private sealed class Tracked(Guid requestId, string protocolKey, string? tool, string? callId)
    {
        public Guid RequestId { get; } = requestId;

        public string ProtocolKey { get; } = protocolKey;

        public string? Tool { get; } = tool;

        public string? CallId { get; } = callId;

        /// <summary>结局已经写出（或重放中已经原样交回）。</summary>
        public bool Delivered { get; set; }

        /// <summary>占着一次看门狗暂停。</summary>
        public bool Waiting { get; set; }

        /// <summary>重放时停机前写出的那个结局。</summary>
        public Resolution? Resolution { get; set; }
    }

    /// <summary>一个请求的结局：交给 CLI 的回答与事件里记下的内容。</summary>
    private sealed record Resolution(CliPermissionStatus Status, string? OptionId, string? Message, Guid? AnsweredBy)
    {
        public static Resolution From(CliRunPermissionRequest row) => new(row.Status, row.OptionId, row.Message, row.AnsweredById);

        public CliPermissionDecision ToDecision() => Status switch
        {
            CliPermissionStatus.Allowed => new CliPermissionDecision(true, OptionId, null),
            CliPermissionStatus.Denied => new CliPermissionDecision(false, OptionId, Message),
            CliPermissionStatus.TimedOut => new CliPermissionDecision(false, null, TimedOutMessage),
            _ => new CliPermissionDecision(false, null, EndedMessage)
        };
    }
}
