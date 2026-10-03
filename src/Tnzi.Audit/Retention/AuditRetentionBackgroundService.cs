namespace Tnzi.Audit.Retention;

/// <summary>
/// 按 <see cref="AuditOptions.RetentionDays"/> 定时删除过期的操作审计，让保留期不再依赖有人记得去点一下。
/// </summary>
/// <remarks>
/// <para>
/// 这是日志表的容量管理，与 <see cref="DataDestructionBackgroundService"/> 管的合规销毁刻意分开：
/// 那边删的是业务数据、每一轮要出证明、晚删漏删都要担责；这边删的是审计操作本身，删漏一轮只是表大一点，
/// 所以这里不出证明、不做空跑、失败只记日志等下一轮。
/// </para>
/// <para>
/// <strong>默认不删</strong>：<see cref="AuditOptions.AutoPurgeEnabled"/> 出厂为 <c>false</c>，服务始终在场但只在
/// 开关打开的那些轮次动手。开关是热设置，所以循环不能在启动时看一眼就退出 —— 关着时它每
/// <see cref="IdleCheckInterval"/> 醒一次看开关，开着时删完睡 <see cref="AuditOptions.AutoPurgeIntervalHours"/>。
/// 「启动时关着就永远不跑」会让配置中心里那个开关打开后什么都不发生，而这种失效没有任何症状。
/// </para>
/// <para>
/// 多实例部署经 <see cref="IDistributedLock"/> 互斥；没有实现时各实例各删各的。删除本身是幂等的
/// （同一条过期行只会被删掉一次），重复只多几条日志，所以这里不像销毁那样在启动时告警。
/// </para>
/// </remarks>
public class AuditRetentionBackgroundService : BackgroundService
{
    /// <summary>多实例互斥用的锁键。</summary>
    internal const string PurgeLockKey = "Tnzi:Audit:RetentionPurge";

    /// <summary>开关关着时重新看一眼开关的间隔。</summary>
    internal static readonly TimeSpan IdleCheckInterval = TimeSpan.FromMinutes(5);

    /// <summary>启动后首次执行前的等待：启动瞬间正是迁移、种子数据与其它后台服务最忙的时候。</summary>
    internal static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<AuditOptions> _options;
    private readonly ILogger<AuditRetentionBackgroundService> _logger;

    /// <summary>
    /// 初始化 <see cref="AuditRetentionBackgroundService"/>。
    /// </summary>
    public AuditRetentionBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<AuditOptions> options,
        ILogger<AuditRetentionBackgroundService> logger)
    {
        _scopeFactory = Check.NotNull(scopeFactory);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var initial = TryReadOptions();
        if (initial?.AutoPurgeEnabled == true)
        {
            _logger.LogInformation(
                "Automatic audit operation purge started: retention {RetentionDays} day(s), interval {IntervalHours} hour(s)",
                initial.RetentionDays,
                initial.AutoPurgeIntervalHours);
        }
        else if (initial is not null)
        {
            // 未启用是常态，用 Debug 而不是 Information，免得成为启动噪音。
            _logger.LogDebug("Automatic audit operation purge is disabled (Audit:AutoPurgeEnabled=false); the loop keeps watching the switch.");
        }

        if (!await DelayAsync(StartupDelay, stoppingToken))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = TryReadOptions();
            var enabled = options?.AutoPurgeEnabled == true;
            if (enabled)
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // 一轮失败不能让服务挂掉：下一轮还要继续，而过期行会一直堆着。
                    _logger.LogError(ex, "Automatic audit operation purge cycle failed.");
                }
            }

            var interval = enabled
                ? TimeSpan.FromHours(Math.Clamp(options!.AutoPurgeIntervalHours, 1, AuditOptions.MaxAutoPurgeIntervalHours))
                : IdleCheckInterval;
            if (!await DelayAsync(interval, stoppingToken))
            {
                break;
            }
        }

        _logger.LogInformation("Automatic audit operation purge stopped");
    }

    /// <summary>
    /// 读当前配置；配置此刻不合法时返回 <c>null</c>（本轮按「关着」处理）并记一条 Error。
    /// </summary>
    /// <remarks>
    /// ★ 带校验器的 <see cref="IOptionsMonitor{TOptions}"/> 在热重载成非法值后，每次读 <c>CurrentValue</c> 都抛
    /// <see cref="OptionsValidationException"/>。在循环的 try 之外读它，异常就逃出 <see cref="ExecuteAsync"/>，
    /// 而后台服务未处理的异常默认把整个宿主停掉 —— 配置中心里一次手误变成一次宕机。
    /// 这里改成跳过本轮、下一轮再读：改回合法值之后自动恢复，不必重启。
    /// 间隔另按 <see cref="AuditOptions.MaxAutoPurgeIntervalHours"/> 夹一次，理由同上（<c>Task.Delay</c> 的上限）。
    /// </remarks>
    private AuditOptions? TryReadOptions()
    {
        try
        {
            return _options.CurrentValue;
        }
        catch (OptionsValidationException ex)
        {
            _logger.LogError(ex, "Automatic audit operation purge skipped: the Audit configuration is currently invalid.");
            return null;
        }
    }

    /// <summary>
    /// 跑一轮：抢锁（立即返回，抢不到即跳过）、按当前的 <see cref="AuditOptions.RetentionDays"/> 删过期行。
    /// </summary>
    /// <returns>本轮删掉的行数；被别的实例抢先则为 <c>0</c>。</returns>
    internal async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        // 每轮重读：这是热设置，管理员刚改的天数应当在下一轮就生效。
        var retentionDays = _options.CurrentValue.RetentionDays;
        if (retentionDays <= 0)
        {
            // 校验器在启动时就挡掉了 <= 0，这里只为热改成非法值时不误删全表。
            _logger.LogWarning("Automatic audit operation purge skipped: Audit:RetentionDays is {RetentionDays}, expected a positive number of days.", retentionDays);
            return 0;
        }

        var distributedLock = scope.ServiceProvider.GetService<IDistributedLock>();
        IDistributedLockHandle? handle = null;
        if (distributedLock is not null)
        {
            handle = await distributedLock.AcquireAsync(PurgeLockKey, timeout: null, cancellationToken);
            if (handle is null || !handle.IsAcquired)
            {
                _logger.LogDebug("Automatic audit operation purge skipped this cycle: another instance holds the lock");
                return 0;
            }
        }

        await using (handle)
        {
            var store = scope.ServiceProvider.GetRequiredService<IAuditStore>();
            var deleted = await store.DeleteExpiredAsync(retentionDays, cancellationToken);
            if (deleted > 0)
            {
                _logger.LogInformation("Automatic audit operation purge removed {Count} operation(s) older than {RetentionDays} day(s)", deleted, retentionDays);
            }

            return deleted;
        }
    }

    /// <summary>等一段时间；被停止则返回 <c>false</c>。</summary>
    private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(delay, stoppingToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
