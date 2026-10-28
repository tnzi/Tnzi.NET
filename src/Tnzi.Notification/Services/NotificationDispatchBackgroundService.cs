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
/// <b>两道防重发</b>：① 交给发送路径之前先做一次条件更新
/// （<c>Scheduled → Sending</c>，影响行数必须为 1），于是两个实例同时扫到也只有一个发得出去；
/// ★★ 这一道与本进程那个定时器<b>两个方向都互斥</b>：定时器先到，<c>SendAsync</c> 自己的认领
/// 已把行改成 <c>Sending</c>，扫描抢不到；扫描先到，定时器的工作项随后进 <c>SendAsync</c>，
/// 对一条 <c>Sending</c> 的消息答 409 而不是再发一遍（认领过的扫描走 <c>SendClaimedAsync</c>）。
/// **不能靠宽限期去挡定时器**：定时器到点只是把工作项<b>入队</b>，而队列是单读者串行执行，
/// 真正开发的时刻取决于积压。② 宽限期（<see cref="DispatchOptions.StuckAfterMinutes"/>）
/// 剩下的作用是别去抢刚刚到点、工作项还在队列里排着的消息 —— 它是节流不是正确性保证。
/// <para>
/// ★★ 卡住批次那一遍<b>也有</b>条件认领（<see cref="TryClaimStalledAsync"/>）：状态本来就是
/// <c>Sending</c>，分不出谁在发，所以租约用的是 <c>LastModificationTime</c> 本身 ——
/// 条件更新把它推到现在、条件里带着「它现在仍然早于 cutoff」。不加列，租约自动到期。
/// </para>
/// </para>
/// <para>
/// ★★ <b>第四遍扫描：排队与延迟重试丢了工作项的消息。</b>默认的「创建并发送」只把工作项写进进程内队列，
/// 延迟重试靠进程内的 <c>Task.Delay</c> 等退避；进程在它们执行之前退出，行就永远停在 <c>Pending</c>。
/// 交给发送的那一刻会写下 <c>ScheduledTime</c>（草稿不写），这一遍接手超过宽限仍是 <c>Pending</c> 的那些。
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
/// 先跨租户取出候选消息的租户清单，再逐个 <c>ICurrentTenant.Change</c> 切进去跑各遍认领与续发，
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
                await LongDelay.DelayAsync(interval, stoppingToken);
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

        if (!_multiTenancyEnabled)
        {
            // 单一逻辑租户：TenantId 列被框架 Ignore，按租户分组没有意义，直接跑一轮。
            await RecoverOnceInCurrentTenantAsync(sp, dispatch, cancellationToken);
            return;
        }

        // ★ 租户清单要跨租户取（IgnoreQueryFilters），否则这一句本身就被过滤成「只有 host」。
        // 只取四遍扫描会关心的那几种状态（排队 / 待重试的 Pending 带 ScheduledTime，草稿不带）；
        // 候选是不是真的到期 / 真的卡住，由切进去之后的那四遍按原判据决定。
        var tenantIds = await repository.AsQueryable()
            .IgnoreQueryFilters()
            .Where(m => m.Status == NotificationStatus.Sending
                        || m.Status == NotificationStatus.Scheduled
                        || (m.Status == NotificationStatus.Pending && m.ScheduledTime != null))
            .Select(m => m.TenantId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var currentTenant = sp.GetRequiredService<ICurrentTenant>();
        foreach (var tenantId in tenantIds)
        {
            if (cancellationToken.IsCancellationRequested) return;

            using (currentTenant.Change(tenantId))
            {
                await RecoverOnceInCurrentTenantAsync(sp, dispatch, cancellationToken);
            }
        }
    }

    /// <summary>在当前租户（或单租户部署的唯一逻辑租户）里跑四遍：每一遍选出候选，逐条认领并续发。</summary>
    /// <remarks>
    /// ★★★ <b>认领发生在发它之前那一刻，一条一条来</b>，而不是一遍开头先把整批都认领下来。
    /// 先整批认领的话，排在后面的那些在等待期间就是「状态 Sending、时间戳停在认领那一刻」的消息：
    /// 前面的消息发得久一点（一次千人群发，或者限速），另一个实例的卡住扫描过了宽限就会合法地接手它，
    /// 而这一轮轮到它时照样发一遍。逐条认领之后，一条消息从被认领起就在发送路径里，心跳由 <c>SendProgress</c> 推进。
    /// </remarks>
    private async Task RecoverOnceInCurrentTenantAsync(
        IServiceProvider sp, DispatchOptions dispatch, CancellationToken cancellationToken)
    {
        var repository = sp.GetRequiredService<IRepository<Message, Guid>>();
        var sender = sp.GetRequiredService<INotificationService>();

        var cutoff = DateTime.UtcNow.AddMinutes(-Math.Max(1, dispatch.StuckAfterMinutes));
        var batchSize = Math.Max(1, dispatch.RecoveryBatchSize);

        await ResumeStalledAsync(repository, sender, cutoff, batchSize, cancellationToken);

        await ResumeDueScheduledAsync(repository, sender, cutoff, batchSize, cancellationToken);

        await ResumeDueDeferredAsync(repository, sender, batchSize, cancellationToken);

        await ResumeOverdueQueuedAsync(repository, sender, cutoff, batchSize, cancellationToken);
    }

    /// <summary>
    /// 逐条认领候选，认领到的立刻交给发送路径；返回认领到的条数。
    /// 认领不到的是别人（另一个实例、本进程的定时器 / 队列）抢先了，安静跳过。
    /// </summary>
    /// <remarks>
    /// 限速不在这里：它落在发送路径里每个收件人的投递上（<see cref="NotificationSendPacer"/>），
    /// 续发与正常发送共用同一份配额。在这里按消息等一下管不住一条几千人的消息。
    /// </remarks>
    private async Task<int> ClaimAndSendEachAsync(
        IReadOnlyList<Guid> candidates, Func<Guid, Task<bool>> tryClaim, INotificationService sender, CancellationToken cancellationToken)
    {
        var resumed = 0;
        foreach (var messageId in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await tryClaim(messageId))
                continue;

            resumed++;
            try
            {
                // 发送路径只发 Pending/Failed 的收件人，所以续发不会重复投递。
                // ★ 刚认领到 Sending 的：交给「已认领」那一条入口。普通的 SendAsync 对 Sending 一律答 409：
                // 那正是它挡住别的入口与这一轮撞车的方式。
                var result = sender is NotificationService owned
                    ? await owned.SendClaimedAsync(messageId, cancellationToken)
                    : await sender.SendAsync(messageId, cancellationToken);
                if (!result.Succeeded)
                {
                    _logger.LogWarning(
                        "Resuming notification {MessageId} did not complete: {Message}",
                        messageId, result.Message);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 一条失败不拖累同批其它消息；它的状态不推进，下一轮还会被扫到。
                _logger.LogError(ex, "Resuming notification {MessageId} threw.", messageId);
            }
        }

        return resumed;
    }

    /// <summary>
    /// 续发停在 <see cref="NotificationStatus.Sending"/> 却超过阈值没有推进的批次。
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
    private async Task ResumeStalledAsync(
        IRepository<Message, Guid> repository, INotificationService sender, DateTime cutoff, int batchSize, CancellationToken cancellationToken)
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

        var resumed = await ClaimAndSendEachAsync(
            candidates, id => TryClaimStalledAsync(repository, id, cutoff, cancellationToken), sender, cancellationToken);

        if (resumed > 0)
        {
            _logger.LogInformation(
                "Resumed {Count} interrupted notification batch(es) that stalled before {Cutoff:u}.",
                resumed, cutoff);
        }
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
    /// 续发「静默时段已经结束、该把那些人发出去了」的消息。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★★ 这是<b>静默时段之所以是延后而不是丢弃</b>的那一半实现。收件人被标成
    /// <see cref="NotificationStatus.Scheduled"/> 并写下 <see cref="Recipient.DeferredUntil"/>，
    /// 消息本身也回到 <c>Scheduled</c>；这一遍找的就是「有收件人的延后时刻已经到了」的消息。
    /// 没有它，那些人会永远排在那里 —— 而这与「到点丢掉」在收件人看来一模一样。
    /// </para>
    /// <para>
    /// ★ <b>这里用「现在」而不是 cutoff。</b>另外几遍的宽限期防的是与本进程那个定时器 / 队列抢
    /// （定时器到点只是入队，真正开发的时刻取决于积压）；延后的时刻是我们自己算出来的
    /// 绝对时刻，没有任何定时器在等它，多等 15 分钟只是让免打扰白白延长。
    /// </para>
    /// <para>
    /// ★ 认领复用 <see cref="TryClaimScheduledAsync"/>（<c>Scheduled → Sending</c>）：
    /// 这一遍和到期定时消息那一遍抢的是同一种状态跃迁，所以两遍同时扫到同一条也只有
    /// 一个发得出去。
    /// </para>
    /// </remarks>
    private async Task ResumeDueDeferredAsync(
        IRepository<Message, Guid> repository, INotificationService sender, int batchSize, CancellationToken cancellationToken)
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

        var resumed = await ClaimAndSendEachAsync(
            candidates, id => TryClaimScheduledAsync(repository, id, cancellationToken), sender, cancellationToken);

        if (resumed > 0)
        {
            _logger.LogInformation(
                "Resumed {Count} notification(s) whose recipients were deferred by their quiet hours, now elapsed.",
                resumed);
        }
    }

    /// <summary>
    /// 续发到期已久却仍停在 <see cref="NotificationStatus.Scheduled"/> 的消息。
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
    /// 卡住扫描的判据要能正确地重新计时。
    /// </para>
    /// <para>
    /// ★ 条件更新绕开变更跟踪器，所以这里<b>不</b>经 <c>IUnitOfWork</c>：它是一次独立的原子写，
    /// 目的正是让它在交给发送路径之前就对其它实例可见。
    /// </para>
    /// </remarks>
    private async Task ResumeDueScheduledAsync(
        IRepository<Message, Guid> repository, INotificationService sender, DateTime cutoff, int batchSize, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        // ★★ 已经发过一轮、只是在等延后到点的消息不是「到期却没人发」。它的 ScheduledTime 还留在行上，
        // 状态也被延后退回了 Scheduled：不排除它，这一遍每个宽限期都会认领它一次：发送路径找不到到期的
        // 收件人，每次空跑一遍（此前还会把它写成 Sent，被延后的人从此收不到）。
        // 它归延后那一遍管，那一遍在延后到点时接手。
        var candidates = await repository.AsQueryable()
            .Where(m => m.Status == NotificationStatus.Scheduled
                        && m.ScheduledTime != null
                        && m.ScheduledTime <= cutoff
                        && !m.Recipients.Any(r => r.Status == NotificationStatus.Scheduled
                                                  && r.DeferredUntil != null
                                                  && r.DeferredUntil > now))
            .OrderBy(m => m.ScheduledTime)
            .Take(batchSize)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);

        var resumed = await ClaimAndSendEachAsync(
            candidates, id => TryClaimScheduledAsync(repository, id, cancellationToken), sender, cancellationToken);

        if (resumed > 0)
        {
            _logger.LogWarning(
                "Sent {Count} scheduled notification(s) that were due before {Cutoff:u} and had no in-process timer left to send them. This is expected after a restart or in a multi-instance deployment.",
                resumed, cutoff);
        }
    }

    /// <summary>
    /// 续发已经交给发送、却超过宽限仍停在 <see cref="NotificationStatus.Pending"/> 的消息：排队发送与延迟重试。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★★★ 两者真正会去发它的都只有<b>进程内</b>的东西：排队是有界内存队列里的一个工作项，延迟重试是一个
    /// <c>Task.Delay</c>。进程在它们执行之前退出（部署、回收），行就永远停在 <c>Pending</c>：另外三遍只看
    /// <c>Sending</c> / <c>Scheduled</c>，「重试失败项」只挑 <c>Failed</c>，管理端只对失败的行给「重发」。
    /// </para>
    /// <para>
    /// ★ <b>判据是 <c>ScheduledTime</c> 有值</b>：交给发送的那一刻写下「最早几点发」（排队 = 当时，重试 = 退避到期），
    /// 草稿（<c>CreateAsync</c>，只创建不发送）不写。只看 <c>Pending</c> 就会把草稿捎带发出去。
    /// </para>
    /// <para>
    /// 宽限期与到期定时消息那一遍同理：别去抢工作项还在队列里排着、或退避刚刚到期的那些。认领是
    /// <c>Pending → Sending</c> 的条件更新，与 <c>SendAsync</c> 自己的认领抢的是同一个跃迁，所以队列迟到的那个
    /// 工作项与这一遍只有一个发得出去。
    /// </para>
    /// </remarks>
    private async Task ResumeOverdueQueuedAsync(
        IRepository<Message, Guid> repository, INotificationService sender, DateTime cutoff, int batchSize, CancellationToken cancellationToken)
    {
        var candidates = await repository.AsQueryable()
            .Where(m => m.Status == NotificationStatus.Pending
                        && m.ScheduledTime != null
                        && m.ScheduledTime <= cutoff)
            .OrderBy(m => m.ScheduledTime)
            .Take(batchSize)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);

        var resumed = await ClaimAndSendEachAsync(
            candidates, id => TryClaimQueuedAsync(repository, id, cancellationToken), sender, cancellationToken);

        if (resumed > 0)
        {
            _logger.LogWarning(
                "Sent {Count} queued or retried notification(s) that were due before {Cutoff:u} and were still Pending: their in-process queue item or retry delay was lost. This is expected after a restart.",
                resumed, cutoff);
        }
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
    internal static Task<bool> TryClaimScheduledAsync(
        IRepository<Message, Guid> repository, Guid id, CancellationToken cancellationToken)
        => MessageClaim.TryClaimAsync(repository, id, NotificationStatus.Scheduled, cancellationToken);

    /// <summary>
    /// 把一条排队 / 待重试的消息从 <see cref="NotificationStatus.Pending"/> 抢到
    /// <see cref="NotificationStatus.Sending"/>。抢到返回 <see langword="true"/>。
    /// </summary>
    internal static Task<bool> TryClaimQueuedAsync(
        IRepository<Message, Guid> repository, Guid id, CancellationToken cancellationToken)
        => MessageClaim.TryClaimAsync(repository, id, NotificationStatus.Pending, cancellationToken);
}
