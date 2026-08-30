using Message = Tnzi.Notification.Entities.Message;

namespace Tnzi.Notification.Services;

/// <summary>
/// 派发恢复后台服务：周期扫出被中断的发送批次并续发完。
/// </summary>
/// <remarks>
/// <para>
/// <b>它补的是哪个洞。</b>收件人状态本来就逐行持久化，所以进程在群发中途退出并<b>不丢数据</b> ——
/// 但也没有任何东西会把它接着发完：消息停在 <see cref="NotificationStatus.Sending"/>，剩下的收件人
/// 停在 <see cref="NotificationStatus.Pending"/>，除非有人手工去点重试。对一次一千人的群发来说，
/// 这等于"发了一半，而且没人知道发到哪了"。
/// </para>
/// <para>
/// <b>幂等靠既有发送路径本身。</b>续发调的就是 <see cref="INotificationService.SendAsync"/>，它只挑
/// <c>Pending</c> / <c>Failed</c> 的收件人 —— 已经 <c>Sent</c> 的不会被重发。恢复只是把它重新触发
/// 一次，不需要另一套逻辑，也就不会与正常路径漂移。
/// </para>
/// <para>
/// <b>只接手真正卡住的。</b>正在正常发送中的消息同样处于 <c>Sending</c>，所以判据是
/// <c>LastModificationTime</c> 超过 <see cref="DispatchOptions.StuckAfterMinutes"/> 仍未推进，
/// 而不是"看见 Sending 就抢"。
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
/// <b>两道防重发</b>：① 交给 <c>SendAsync</c> 之前先做一次条件更新
/// （<c>Scheduled → Sending</c>，影响行数必须为 1），于是两个实例同时扫到也只有一个发得出去；
/// ★★ 这一道**同时**挡住本进程那个定时器 —— 前提是 <c>SendAsync</c> 在进入收件人循环之前
/// 就把 <c>Sending</c> 落了库（它现在会）。**不能靠宽限期去挡定时器**：定时器到点只是把工作项
/// <b>入队</b>，而队列是单读者串行执行，真正开发的时刻取决于积压；一次几十分钟的群发期间，
/// 库里那行会一直写着 <c>Scheduled</c>。② 宽限期（<see cref="DispatchOptions.StuckAfterMinutes"/>）
/// 剩下的作用是别去抢刚刚到点、工作项还在队列里排着的消息 —— 它是节流不是正确性保证。
/// <para>
/// 卡住批次那一遍没有条件认领这层保护（状态本来就是 <c>Sending</c>，条件更新分不出谁在发），
/// 那是既有形态，要根治得给消息加租约列。
/// </para>
/// </para>
/// <para>
/// <b>失败只记日志不崩服务</b> —— 与框架其它遥测/派发后台服务同款取舍：一批失败丢这一批，
/// 下一轮扫描会再次遇到它（状态没推进），而让整个后台服务崩掉会让所有后续批次都停摆。
/// </para>
/// </remarks>
public class NotificationDispatchBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptionsMonitor<NotificationOptions> _options;
    private readonly ILogger<NotificationDispatchBackgroundService> _logger;

    /// <summary>
    /// 启动后的首次扫描延迟。给宿主留出完成迁移与预热的时间：启动瞬间就去抢一批
    /// 「看起来卡住」的消息，只会和一个还没跑起来的发送管线打架。
    /// </summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    public NotificationDispatchBackgroundService(
        IServiceProvider serviceProvider,
        IOptionsMonitor<NotificationOptions> options,
        ILogger<NotificationDispatchBackgroundService> logger)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
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
        var sender = sp.GetRequiredService<INotificationService>();

        var cutoff = DateTime.UtcNow.AddMinutes(-Math.Max(1, dispatch.StuckAfterMinutes));
        var batchSize = Math.Max(1, dispatch.RecoveryBatchSize);

        // 被中断的批次：停在 Sending 且超过阈值没有推进。
        // 用 LastModificationTime，缺失时回退 CreationTime（消息创建后一次都没写过）。
        var stuck = await repository.AsQueryable()
            .Where(m => m.Status == NotificationStatus.Sending
                        && (m.LastModificationTime ?? m.CreationTime) < cutoff)
            .OrderBy(m => m.CreationTime)
            .Take(batchSize)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);

        if (stuck.Count > 0)
        {
            _logger.LogInformation(
                "Resuming {Count} interrupted notification batch(es) that stalled before {Cutoff:u}.",
                stuck.Count, cutoff);
        }

        var due = await ClaimDueScheduledAsync(repository, cutoff, batchSize, cancellationToken);

        if (stuck.Count == 0 && due.Count == 0)
            return;

        var pacer = new SendPacer(dispatch.RatePerMinute);

        foreach (var messageId in stuck.Concat(due))
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
