using AuditErrorCodes = Tnzi.Audit.Metadata.ErrorCodes;

namespace Tnzi.Audit.Retention;

/// <summary>
/// 定时跑数据销毁：让「到期即销毁」不依赖任何人记得去点一下。
/// </summary>
/// <remarks>
/// <para>
/// 合规要求里的「自动」正是这个意思——一份需要人工触发的销毁流程，
/// 在忙起来的那几周一定不会被执行，而那几周恰恰是最需要它的时候。
/// </para>
/// <para>
/// <strong>多实例部署要靠 <see cref="IDistributedLock"/> 互斥。</strong>
/// 没有实现时退化为无互斥并在启动时告警：两个实例同时扫描会各自出一份证明，
/// 其中一份必然是「销毁了 0 条」——事后读证明的人无从判断那是没到期还是被别人抢先了。
/// 锁本身住在 <see cref="DataDestructionService.RunAsync"/> 里（手动端点与定时轮共用同一把），
/// 本服务只把「抢不到」（错误码 <see cref="AuditErrorCodes.AuditDestructionRunInProgress"/>）当作「这一轮跳过」。
/// ★ 判据是错误码不是 409：同名策略与锁中途丢失也答 409，按状态码判会把一个配置错误
/// 从每轮一条 Error 降成一条「别人持锁」的 Debug —— 什么都不销毁，而默认日志级别下一个字都看不见。
/// </para>
/// </remarks>
public class DataDestructionBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<DataDestructionOptions> _options;
    private readonly ILogger<DataDestructionBackgroundService> _logger;

    /// <summary>
    /// 初始化 <see cref="DataDestructionBackgroundService"/>。
    /// </summary>
    public DataDestructionBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<DataDestructionOptions> options,
        ILogger<DataDestructionBackgroundService> logger)
    {
        _scopeFactory = Check.NotNull(scopeFactory);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.CurrentValue.Enabled)
        {
            // 未启用是常态（这是可选能力），用 Debug 而不是 Information，免得成为启动噪音。
            _logger.LogDebug("Policy-driven data destruction is disabled.");
            return;
        }

        _logger.LogInformation(
            "Policy-driven data destruction started, interval: {Interval} hour(s), dry run: {DryRun}",
            _options.CurrentValue.IntervalHours,
            _options.CurrentValue.DryRun);

        await WarnIfDestructionCannotBeSerialisedAsync();

        // 启动后先等一会儿再首次执行：启动瞬间正是迁移、种子数据与其它后台服务最忙的时候，
        // 而这项工作晚几分钟没有任何影响。
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // 一轮失败不能让服务挂掉：下一轮还要继续，而到期数据会一直堆着。
                _logger.LogError(ex, "Data destruction cycle failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(Math.Max(1, _options.CurrentValue.IntervalHours)), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Policy-driven data destruction stopped");
    }

    /// <summary>
    /// 无法互斥时告警一次。运维必须知道自己处在哪种模式，而不该靠读源码发现。
    /// </summary>
    private async Task WarnIfDestructionCannotBeSerialisedAsync()
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        if (scope.ServiceProvider.GetService<IDistributedLock>() is not null)
        {
            return;
        }

        _logger.LogWarning(
            "Data destruction is running without an IDistributedLock implementation. This is correct for a "
            + "single instance, but in a multi-instance deployment every instance scans the same batch and "
            + "writes its own destruction certificate, leaving certificates that report zero records for no "
            + "discernible reason. Load a module that provides IDistributedLock (e.g. Tnzi.Redis).");
    }

    /// <summary>
    /// 一个周期内最多连续跑几轮。
    /// </summary>
    /// <remarks>
    /// 一轮里每条策略最多销毁 <see cref="DataDestructionOptions.BatchSize"/> 条（一条短语句，持锁时间有界）；
    /// 还有剩余（<c>HasMore</c>）时本周期接着跑下一轮，而不是睡满 <see cref="DataDestructionOptions.IntervalHours"/> 再来：
    /// 否则每天到期量超过 BatchSize 的策略积压只会线性增长，首次启用时的历史存量要几年才销毁完。
    /// 上限挡的是「销毁器没让行离开到期集合」（例如只做匿名化而不改时间戳也不在 Scope 里排除它）：
    /// 那时每一轮都会拿回同一批行，没有上限就是一个不停写证明的死循环。
    /// </remarks>
    internal const int MaxRunsPerCycle = 20;

    /// <summary>
    /// 跑一个周期。互斥由 <see cref="DataDestructionService.RunAsync"/> 内部抢（timeout: null，立即返回）：
    /// 抢不到说明另一个实例（或一次手动 Run）正在跑这一轮——跳过就好，下一个周期会再来；
    /// 排队等锁只会让所有实例挤在同一时刻醒来。
    /// </summary>
    /// <remarks>
    /// 只要上一轮有策略真的销毁了东西且报告还有剩余，就在同一周期里接着跑，最多 <see cref="MaxRunsPerCycle"/> 轮。
    /// 空跑不续：空跑什么都不删，下一轮拿回的是同一批。每一轮用新的作用域，不让变更跟踪器跨轮堆积。
    /// </remarks>
    internal async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        for (var run = 1; run <= MaxRunsPerCycle; run++)
        {
            stoppingToken.ThrowIfCancellationRequested();

            await using var scope = _scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetService<IDataDestructionService>();
            if (service == null)
            {
                _logger.LogWarning("IDataDestructionService is not registered; skipping this cycle.");
                return;
            }

            var outcome = await RunAndLogAsync(service, stoppingToken);
            if (outcome == null || !HasBacklog(outcome))
            {
                return;
            }
        }

        _logger.LogInformation(
            "Data destruction stopped after {Runs} consecutive runs with records still pending; the next cycle continues.",
            MaxRunsPerCycle);
    }

    /// <summary>
    /// 上一轮之后是否还该接着跑：非空跑，且有策略本轮确实销毁了记录、并报告还有剩余。
    /// </summary>
    /// <remarks>
    /// 「确实销毁了」这一条不能省：整批都被保全或者销毁器一条没删时接着跑只会原地打转。
    /// </remarks>
    private static bool HasBacklog(DataDestructionRunDto run)
        => !run.IsDryRun && run.Policies.Any(p => p.Error == null && p.HasMore && p.DestroyedCount > 0);

    private async Task<DataDestructionRunDto?> RunAndLogAsync(IDataDestructionService service, CancellationToken stoppingToken)
    {
        var result = await service.RunAsync(stoppingToken);

        if (!result.Succeeded)
        {
            if (result.ErrorCode == AuditErrorCodes.AuditDestructionRunInProgress)
            {
                _logger.LogDebug("Data destruction skipped this cycle: another run holds the lock");
                return null;
            }

            // 其余失败一律 Error，包括同名策略（409）与锁中途丢失（409）：前者是每轮都不会自愈的配置错误，
            // 后者是策略跑了一半。两者与「跳过这一轮」只在错误码上有区别。
            _logger.LogError("Data destruction cycle failed: {Message}", result.Message);
            return null;
        }

        var run = result.Data;
        if (run == null)
        {
            return null;
        }

        var failed = run.Policies.Where(p => p.Error != null).ToList();
        foreach (var policy in failed)
        {
            _logger.LogError(
                "Retention policy '{PolicyName}' failed: {Error}", policy.PolicyName, policy.Error);
        }

        if (run.TotalDestroyed > 0 || run.TotalHeld > 0)
        {
            _logger.LogInformation(
                "Data destruction cycle completed: {Destroyed} destroyed, {Held} held by litigation hold{DryRun}",
                run.TotalDestroyed, run.TotalHeld, run.IsDryRun ? " [dry run]" : string.Empty);
        }

        return run;
    }
}
