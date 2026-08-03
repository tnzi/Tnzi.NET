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

    /// <summary>一轮恢复扫描。</summary>
    private async Task RecoverOnceAsync(DispatchOptions dispatch, CancellationToken cancellationToken)
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

        if (stuck.Count == 0)
            return;

        _logger.LogInformation(
            "Resuming {Count} interrupted notification batch(es) that stalled before {Cutoff:u}.",
            stuck.Count, cutoff);

        var pacer = new SendPacer(dispatch.RatePerMinute);

        foreach (var messageId in stuck)
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
