namespace Tnzi.AI.Cli.Dispatch;

/// <summary>
/// 停下单个任务（子 agent、后台命令）的通道：使用方的请求由任一服务端副本追加到 <see cref="CliRun.StopTaskRequests"/>，
/// 执行这条运行的副本在这里轮询到新的任务 id，把 <c>stop_task</c> 写给 CLI。
/// </summary>
/// <remarks>
/// <para>
/// 任务停下之后 CLI 照常发它的结局（<c>task_notification</c>，<c>stopped</c>），任务事件与界面都从那里得知；这里不落任何事件。
/// </para>
/// <para>
/// 同一个 id 只写一次（按这一次尝试记）。重新附着、或交接给下一条运行之后，新的一次尝试会把已经写过的 id 再写一遍：
/// CLI 对已经结束的任务答一个错误，适配器把它记成一条警告日志事件，不停别的任何东西。
/// </para>
/// </remarks>
internal sealed class CliRunTaskStops
{
    private readonly Guid _runId;
    private readonly ICliTaskControl _control;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CliRunSignalHub _signalHub;
    private readonly TimeSpan _pollInterval;
    private readonly ILogger _logger;
    private readonly HashSet<string> _sent = new(StringComparer.Ordinal);

    /// <param name="runId">运行。</param>
    /// <param name="control">这一次尝试的适配器。</param>
    /// <param name="scopeFactory">每次读库开一个作用域（同事件落库）。</param>
    /// <param name="signalHub">请求落在本副本时的提示。</param>
    /// <param name="pollInterval">请求落在别的副本时多久查一次。</param>
    /// <param name="logger">日志。</param>
    public CliRunTaskStops(
        Guid runId, ICliTaskControl control, IServiceScopeFactory scopeFactory, CliRunSignalHub signalHub, TimeSpan pollInterval, ILogger logger)
    {
        _runId = runId;
        _control = Check.NotNull(control);
        _scopeFactory = Check.NotNull(scopeFactory);
        _signalHub = Check.NotNull(signalHub);
        _pollInterval = pollInterval > TimeSpan.Zero ? pollInterval : TimeSpan.FromSeconds(1);
        _logger = Check.NotNull(logger);
    }

    /// <summary>后台循环：查有没有新的停下请求，有就写给 CLI。随这一次尝试结束（<paramref name="cancellationToken"/>）。</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await DeliverNewAsync(cancellationToken);
                await _signalHub.WaitAsync(_runId, _pollInterval, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // 一次查库失败不能让请求丢掉：下一轮再试（已经写出的 id 记在内存里，不会重写）
                _logger.LogWarning(ex, "Could not check the task stop requests of CLI run {RunId}; retrying", _runId);
                await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    /// <summary>读一次请求，把还没写过的写给 CLI。返回这一次写出的任务 id。</summary>
    internal async Task<IReadOnlyList<string>> DeliverNewAsync(CancellationToken cancellationToken)
    {
        string? requests;
        using (var scope = _scopeFactory.CreateScope())
        {
            requests = await scope.ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>().AsQueryable()
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(r => r.Id == _runId)
                .Select(r => r.StopTaskRequests)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (string.IsNullOrEmpty(requests))
        {
            return [];
        }

        var delivered = new List<string>();
        foreach (var taskId in requests.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (_sent.Contains(taskId))
            {
                continue;
            }

            if (!await _control.StopTaskAsync(taskId, cancellationToken))
            {
                // 进程已经不再读：这一次尝试写不了，留给下一次（或者运行已经在结束）
                break;
            }

            _sent.Add(taskId);
            delivered.Add(taskId);
            _logger.LogInformation("CLI run {RunId}: asked the CLI to stop task {TaskId}", _runId, taskId);
        }

        return delivered;
    }
}
