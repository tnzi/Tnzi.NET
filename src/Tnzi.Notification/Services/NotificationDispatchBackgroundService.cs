using Message = Tnzi.Notification.Entities.Message;

namespace Tnzi.Notification.Services;

/// <summary>
/// 派发恢复后台服务：周期扫出被中断的发送批次并续发完。
/// </summary>
/// <remarks>
/// <para>
/// <b>它补的是哪个洞。</b>进程在群发中途退出时没有任何东西会把它接着发完：消息停在
/// <see cref="NotificationStatus.Sending"/>，剩下的收件人停在 <see cref="NotificationStatus.Pending"/>，
/// 除非有人手工去点重试。对一次一千人的群发来说，这等于"发了一半，而且没人知道发到哪了"。
/// </para>
/// <para>
/// ★★ <b>「已经发到哪了」由 <c>SendAsync</c> 循环内的分片落库保证</b>（见 <c>SendProgress</c>）。
/// 这一点曾经只是一句注释：整批收件人状态由循环结束后<b>一次</b> <c>SaveChangesAsync</c> 落库，
/// 于是崩溃时库里所有人都还是 <c>Pending</c>，续发把整份名单重发一遍。现在最多重发最后一片。
/// </para>
/// <para>
/// <b>幂等靠既有发送路径本身。</b>续发调的就是 <see cref="INotificationService.SendAsync"/>，它只挑
/// <c>Pending</c> / <c>Failed</c> 的收件人 —— 已经 <c>Sent</c> 的不会被重发。恢复只是把它重新触发
/// 一次，不需要另一套逻辑，也就不会与正常路径漂移。
/// </para>
/// <para>
/// <b>只接手真正卡住的。</b>正在正常发送中的消息同样处于 <c>Sending</c>，所以判据是
/// <c>LastModificationTime</c> 超过 <see cref="DispatchOptions.StuckAfterMinutes"/> 仍未推进，
/// 而不是"看见 Sending 就抢"。★ 这个判据只有在<b>正在飞的批次会推进那个时间戳</b>时才成立 ——
/// 心跳由 <c>SendProgress</c> 在收件人循环内落库时顺带完成。
/// </para>
/// <para>
/// ★★ <b>第二遍扫描：到期却没人发的定时消息。</b><see cref="INotificationService.CreateAndSendAsync"/>
/// 把定时消息的行落库（<c>Scheduled</c> + <c>ScheduledTime</c>）之后，真正会去发它的只有
/// <see cref="ChannelQueueService.EnqueueWithDelayAsync"/> 起的一个<b>进程内</b>定时器。进程一停
/// 那个定时器就没了，而多实例部署里它从一开始就只存在于接下这次创建请求的那一个实例上。
/// 数据一行不丢，只是永远不会被发出去 —— 并且它会一直好端端地列在"已排期"里，
/// 这比"消息不见了"更容易被当真。
/// </para>
/// <para>
/// ★★ <b>第三遍扫描：静默时段已经结束的收件人。</b>用户设的免打扰表达的是<b>时机</b>，
/// 所以落在那个窗口里的收件人被<b>延后</b>（<c>Recipient.DeferredUntil</c>）而不是丢弃。
/// 这一遍把到期的那些接着发完 —— 没有它，「延后」在收件人看来与「到点丢掉」一模一样。
/// </para>
/// <para>
/// <b>两道防重发</b>：① 交给 <c>SendAsync</c> 之前先做一次条件更新
/// （<c>Scheduled → Sending</c>，影响行数必须为 1），于是两个实例同时扫到也只有一个发得出去；
/// ★★ 这一道**同时**挡住本进程那个定时器 —— 前提是 <c>SendAsync</c> 在进入收件人循环之前
/// 就把 <c>Sending</c> 落了库（它现在会）。**不能靠宽限期去挡定时器**：定时器到点只是把工作项
/// <b>入队</b>，而队列是单读者串行执行，真正开发的时刻取决于积压；一次几十分钟的群发期间，
/// 库里那行会一直写着 <c>Scheduled</c>。② 宽限期（<see cref="DispatchOptions.StuckAfterMinutes"/>）
/// 剩下的作用是别去抢刚刚到点、工作项还在队列里排着的消息 —— 它是节流不是正确性保证。
/// <para>
/// ★★ 卡住批次那一遍<b>也有</b>条件认领（<see cref="TryClaimStalledAsync"/>）：状态本来就是
/// <c>Sending</c>，分不出谁在发，所以租约用的是 <c>LastModificationTime</c> 本身 ——
/// 条件更新把它推到现在、条件里带着「它现在仍然早于 cutoff」。不加列，租约自动到期。
/// </para>
/// </para>
/// <para>
/// <b>失败只记日志不崩服务</b> —— 与框架其它遥测/派发后台服务同款取舍：一批失败丢这一批，
/// 下一轮扫描会再次遇到它（状态没推进），而让整个后台服务崩掉会让所有后续批次都停摆。
/// </para>
/// <para>
/// ★★ <b>多租户开启时逐租户跑。</b><c>Message</c> 的全局过滤器是严格等值 <c>TenantId == 当前租户</c>，
/// 而这个后台作用域里没有租户 —— 三遍扫描此前看到的只有 host 级（<c>TenantId == null</c>）的消息，
/// 每个租户的卡住批次 / 到期定时消息 / 延后的收件人一条都扫不到，且零日志。修法与
/// <c>FileCleanupService</c> / <c>DataDestructionService</c> 的 <c>ForEachTenantAsync</c> 同形：
/// 先跨租户取出候选消息的租户清单，再逐个 <c>ICurrentTenant.Change</c> 切进去跑三遍认领与续发，
/// 让过滤器在该租户范围内照常生效 —— 不是关掉过滤器。
/// </para>
/// </remarks>
public class NotificationDispatchBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptionsMonitor<NotificationOptions> _options;
    private readonly ILogger<NotificationDispatchBackgroundService> _logger;
    private readonly bool _multiTenancyEnabled;

    /// <summary>
    /// 启动后的首次扫描延迟。给宿主留出完成迁移与预热的时间：启动瞬间就去抢一批
    /// 「看起来卡住」的消息，只会和一个还没跑起来的发送管线打架。
    /// </summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    public NotificationDispatchBackgroundService(
        IServiceProvider serviceProvider,
        IOptionsMonitor<NotificationOptions> options,
        ILogger<NotificationDispatchBackgroundService> logger,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
        _multiTenancyEnabled = multiTenancyOptions?.Value.Enabled ?? false;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var dispatch = _options.CurrentValue.Dispatch;

            if (dispatch.EnableRecovery)
            {
                try
                {
                    await RecoverOnceAsync(dispatch, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // 记日志后继续：下一轮会再遇到这些消息（它们的状态没有推进）。
                    _logger.LogError(ex, "Notification dispatch recovery pass failed; will retry next interval.");
                }
            }

            var interval = TimeSpan.FromMinutes(Math.Max(1, dispatch.RecoveryIntervalMinutes));
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>一轮恢复扫描。<c>internal</c> 是为了让测试能跑单独一轮，而不必等 30 秒的启动延迟。</summary>
    internal async Task RecoverOnceAsync(DispatchOptions dispatch, CancellationToken cancellationToken)
    {
        // 后台服务是 Singleton，仓储是 Scoped —— 每轮一个作用域。
        using var scope = _serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        var repository = sp.GetRequiredService<IRepository<Message, Guid>>();

        var pacer = new SendPacer(dispatch.RatePerMinute);

        if (!_multiTenancyEnabled)
        {
            // 单一逻辑租户：TenantId 列被框架 Ignore，按租户分组没有意义，直接跑一轮。
            await RecoverOnceInCurrentTenantAsync(sp, dispatch, pacer, cancellationToken);
            return;
        }

        // ★ 租户清单要跨租户取（IgnoreQueryFilters），否则这一句本身就被过滤成「只有 host」。
        // 只取三遍扫描会关心的两种状态；候选是不是真的到期 / 真的卡住，由切进去之后的那三遍按原判据决定。
        var tenantIds = await repository.AsQueryable()
            .IgnoreQueryFilters()
            .Where(m => m.Status == NotificationStatus.Sending || m.Status == NotificationStatus.Scheduled)
            .Select(m => m.TenantId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var currentTenant = sp.GetRequiredService<ICurrentTenant>();
        foreach (var tenantId in tenantIds)
        {
            if (cancellationToken.IsCancellationRequested) return;

            using (currentTenant.Change(tenantId))
            {
                await RecoverOnceInCurrentTenantAsync(sp, dispatch, pacer, cancellationToken);
            }
        }
    }

    /// <summary>在当前租户（或单租户部署的唯一逻辑租户）里跑三遍认领并续发。</summary>
    private async Task RecoverOnceInCurrentTenantAsync(
        IServiceProvider sp, DispatchOptions dispatch, SendPacer pacer, CancellationToken cancellationToken)
    {
        var repository = sp.GetRequiredService<IRepository<Message, Guid>>();
        var sender = sp.GetRequiredService<INotificationService>();

        var cutoff = DateTime.UtcNow.AddMinutes(-Math.Max(1, dispatch.StuckAfterMinutes));
        var batchSize = Math.Max(1, dispatch.RecoveryBatchSize);

        var stuck = await ClaimStalledAsync(repository, cutoff, batchSize, cancellationToken);

        var due = await ClaimDueScheduledAsync(repository, cutoff, batchSize, cancellationToken);

        var deferred = await ClaimDueDeferredAsync(repository, batchSize, cancellationToken);

        if (stuck.Count == 0 && due.Count == 0 && deferred.Count == 0)
            return;

        foreach (var messageId in stuck.Concat(due).Concat(deferred))
        {
            if (cancellationToken.IsCancellationRequested) return;

            await pacer.WaitAsync(cancellationToken);
            try
            {
                // SendAsync 只发 Pending/Failed 的收件人，所以续发不会重复投递。
                var result = await sender.SendAsync(messageId, cancellationToken);
                if (!result.Succeeded)
                {
                    _logger.LogWarning(
                        "Resuming notification {MessageId} did not complete: {Message}",
                        messageId, result.Message);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // 一条失败不拖累同批其它消息；它的状态不推进，下一轮还会被扫到。
                _logger.LogError(ex, "Resuming notification {MessageId} threw.", messageId);
            }
        }
    }

    /// <summary>
    /// 认领停在 <see cref="NotificationStatus.Sending"/> 却超过阈值没有推进的批次。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★★ <b>这一遍此前没有认领，只有 SELECT。</b>两个实例同一分钟扫到同一条卡住的批次
    /// 会各调一次 <c>SendAsync</c>，而它们看到的收件人是同一批 —— 那是一次重复投递，
    /// 也是本模块最贵的失败形态。定时消息那一遍靠「<c>Scheduled → Sending</c> 只能成功一次」
    /// 挡住这件事，卡住批次这一遍没有对应的状态跃迁可用（状态本来就是 <c>Sending</c>）。
    /// </para>
    /// <para>
    /// ★ <b>租约就用 <c>LastModificationTime</c> 本身</b>，不加列（加列 = 每个消费方一次迁移）。
    /// 认领 = 一次条件更新：把时间戳推到现在，条件里带着「它现在仍然早于 cutoff」。
    /// 谁先 UPDATE 谁拿到；后到的那次条件已不成立，影响 0 行，安静跳过。
    /// 认领方随后若崩掉，时间戳停在认领那一刻，再过一个
    /// <see cref="DispatchOptions.StuckAfterMinutes"/> 它会重新可认领 —— 租约自动到期，
    /// 不需要额外的释放动作。
    /// </para>
    /// <para>
    /// ★ 这道租约与 <c>SendAsync</c> 循环内的心跳（见 <c>SendProgress</c>）是一对：
    /// 心跳保证<b>正在飞</b>的批次不会走到 cutoff 之前，租约保证<b>真的卡住</b>的批次
    /// 只被一个扫描接手。少任何一半都还是重复投递。
    /// </para>
    /// </remarks>
    internal async Task<List<Guid>> ClaimStalledAsync(
        IRepository<Message, Guid> repository, DateTime cutoff, int batchSize, CancellationToken cancellationToken)
    {
        // 被中断的批次：停在 Sending 且超过阈值没有推进。
        // 用 LastModificationTime，缺失时回退 CreationTime（消息创建后一次都没写过）。
        var candidates = await repository.AsQueryable()
            .Where(m => m.Status == NotificationStatus.Sending
                        && (m.LastModificationTime ?? m.CreationTime) < cutoff)
            .OrderBy(m => m.CreationTime)
            .Take(batchSize)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
            return [];

        var claimed = new List<Guid>(candidates.Count);
        foreach (var id in candidates)
        {
            if (await TryClaimStalledAsync(repository, id, cutoff, cancellationToken))
                claimed.Add(id);
        }

        if (claimed.Count > 0)
        {
            _logger.LogInformation(
                "Resuming {Count} interrupted notification batch(es) that stalled before {Cutoff:u}.",
                claimed.Count, cutoff);
        }

        return claimed;
    }

    /// <summary>
    /// 把一条卡住的批次的租约抢过来：影响行数为 1 即认领成功。
    /// </summary>
    /// <remarks>
    /// ★ <b>cutoff 条件必须留在 UPDATE 里</b>，与 <see cref="TryClaimScheduledAsync"/> 同理：
    /// 上一步的 SELECT 只说明「刚才它是卡住的」，两个实例都会得到这个结论。
    /// </remarks>
    internal static async Task<bool> TryClaimStalledAsync(
        IRepository<Message, Guid> repository, Guid id, DateTime cutoff, CancellationToken cancellationToken)
    {
        var affected = await repository.AsQueryable()
            .Where(m => m.Id == id
                        && m.Status == NotificationStatus.Sending
                        && (m.LastModificationTime ?? m.CreationTime) < cutoff)
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.LastModificationTime, DateTime.UtcNow),
                cancellationToken);

        return affected == 1;
    }

    /// <summary>
    /// 认领「静默时段已经结束、该把那些人发出去了」的消息。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★★ 这是<b>静默时段之所以是延后而不是丢弃</b>的那一半实现。收件人被标成
    /// <see cref="NotificationStatus.Scheduled"/> 并写下 <see cref="Recipient.DeferredUntil"/>，
    /// 消息本身也回到 <c>Scheduled</c>；这一遍找的就是「有收件人的延后时刻已经到了」的消息。
    /// 没有它，那些人会永远排在那里 —— 而这与「到点丢掉」在收件人看来一模一样。
    /// </para>
    /// <para>
    /// ★ <b>这里用「现在」而不是 cutoff。</b>另外两遍的宽限期防的是与本进程那个定时器抢
    /// （定时器到点只是入队，真正开发的时刻取决于积压）；延后的时刻是我们自己算出来的
    /// 绝对时刻，没有任何定时器在等它，多等 15 分钟只是让免打扰白白延长。
    /// </para>
    /// <para>
    /// ★ 认领复用 <see cref="TryClaimScheduledAsync"/>（<c>Scheduled → Sending</c>）：
    /// 这一遍和到期定时消息那一遍抢的是同一种状态跃迁，所以两遍同时扫到同一条也只有
    /// 一个发得出去。
    /// </para>
    /// </remarks>
    internal async Task<List<Guid>> ClaimDueDeferredAsync(
        IRepository<Message, Guid> repository, int batchSize, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var candidates = await repository.AsQueryable()
            .Where(m => m.Status == NotificationStatus.Scheduled
                        && m.Recipients.Any(r => r.Status == NotificationStatus.Scheduled
                                                 && r.DeferredUntil != null
                                                 && r.DeferredUntil <= now))
            .OrderBy(m => m.CreationTime)
            .Take(batchSize)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
            return [];

        var claimed = new List<Guid>(candidates.Count);
        foreach (var id in candidates)
        {
            if (await TryClaimScheduledAsync(repository, id, cancellationToken))
                claimed.Add(id);
        }

        if (claimed.Count > 0)
        {
            _logger.LogInformation(
                "Resuming {Count} notification(s) whose recipients were deferred by their quiet hours, now elapsed.",
                claimed.Count);
        }

        return claimed;
    }

    /// <summary>
    /// 认领到期已久却仍停在 <see cref="NotificationStatus.Scheduled"/> 的消息。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 认领方式是<b>条件更新</b>：<c>UPDATE … SET Status = Sending WHERE Id = @id AND
    /// Status = Scheduled</c>。影响行数为 0 说明别人抢先了（另一个实例的扫描，或者本进程那个
    /// 还活着的定时器刚开始发），这一条就跳过。没有这一步，两个实例同一分钟扫到同一条
    /// 定时消息会各发一遍 —— 而重复投递正是本模块最贵的失败形态。
    /// </para>
    /// <para>
    /// 顺带把 <c>LastModificationTime</c> 推到现在：认领之后这条消息就是一条"正在发送"的消息，
    /// 卡住扫描的判据要能正确地重新计时，否则一条刚被认领的消息可能在同一轮里
    /// 又被当成"卡了很久的 Sending"。
    /// </para>
    /// <para>
    /// ★ 条件更新绕开变更跟踪器，所以这里<b>不</b>经 <c>IUnitOfWork</c>：它是一次独立的原子写，
    /// 目的正是让它在交给 <c>SendAsync</c> 之前就对其它实例可见。
    /// </para>
    /// </remarks>
    internal async Task<List<Guid>> ClaimDueScheduledAsync(
        IRepository<Message, Guid> repository, DateTime cutoff, int batchSize, CancellationToken cancellationToken)
    {
        var candidates = await repository.AsQueryable()
            .Where(m => m.Status == NotificationStatus.Scheduled
                        && m.ScheduledTime != null
                        && m.ScheduledTime <= cutoff)
            .OrderBy(m => m.ScheduledTime)
            .Take(batchSize)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
            return [];

        var claimed = new List<Guid>(candidates.Count);
        foreach (var id in candidates)
        {
            if (await TryClaimScheduledAsync(repository, id, cancellationToken))
                claimed.Add(id);
        }

        if (claimed.Count > 0)
        {
            _logger.LogWarning(
                "Sending {Count} scheduled notification(s) that were due before {Cutoff:u} and had no in-process timer left to send them. This is expected after a restart or in a multi-instance deployment.",
                claimed.Count, cutoff);
        }

        return claimed;
    }

    /// <summary>
    /// 把一条消息从 <see cref="NotificationStatus.Scheduled"/> 抢到
    /// <see cref="NotificationStatus.Sending"/>。抢到返回 <see langword="true"/>。
    /// </summary>
    /// <remarks>
    /// ★ <b>条件必须写在 UPDATE 里</b>，不能靠上一步的 SELECT。两个实例会在同一分钟各自
    /// 选出同一条，谁先 UPDATE 谁发；后到的那次影响 0 行，于是安静地跳过。
    /// 把条件留在 SELECT 上等于两边都认为自己抢到了 —— 那就是一条重复投递。
    /// </remarks>
    internal static async Task<bool> TryClaimScheduledAsync(
        IRepository<Message, Guid> repository, Guid id, CancellationToken cancellationToken)
    {
        var affected = await repository.AsQueryable()
            .Where(m => m.Id == id && m.Status == NotificationStatus.Scheduled)
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.Status, NotificationStatus.Sending)
                      .SetProperty(m => m.LastModificationTime, DateTime.UtcNow),
                cancellationToken);

        return affected == 1;
    }
}

/// <summary>
/// 发送节奏器：把发送摊到每分钟不超过 N 次。
/// </summary>
/// <remarks>
/// 群发不限速触发的不是"发得慢"，而是<b>整个发送账号被服务商的滥用防护封停</b> ——
/// 连同密码重置这类事务邮件一起停摆。<c>RatePerMinute</c> 为 0 表示不限速（默认），
/// 因为对没有群发场景的应用，凭空引入等待是纯粹的损失。
/// </remarks>
internal sealed class SendPacer(int ratePerMinute)
{
    private readonly TimeSpan _interval = ratePerMinute > 0
        ? TimeSpan.FromMilliseconds(60_000d / ratePerMinute)
        : TimeSpan.Zero;

    private DateTime _lastSentUtc = DateTime.MinValue;

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        if (_interval <= TimeSpan.Zero)
            return;

        var elapsed = DateTime.UtcNow - _lastSentUtc;
        if (elapsed < _interval)
            await Task.Delay(_interval - elapsed, cancellationToken);

        _lastSentUtc = DateTime.UtcNow;
    }
}
