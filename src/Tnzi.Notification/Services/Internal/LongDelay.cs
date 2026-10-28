namespace Tnzi.Notification.Services.Internal;

/// <summary>
/// 等一段可能很长的时间，而不让 <c>Task.Delay</c> 越界抛出。
/// </summary>
/// <remarks>
/// <para>
/// ★★ 单次 <c>Task.Delay</c> 不接受超过 Timer 上限（约 49.7 天）的时长，超过即抛 <c>ArgumentOutOfRangeException</c>。
/// 本模块有三处等待的时长来自配置或数据：派发恢复的扫描间隔（<c>Dispatch:RecoveryIntervalMinutes</c>，设置中心只钉了下限）、
/// 传真回执的轮询间隔（<c>PollIntervalSeconds</c>，校验器只钉了下限）、定时消息的进程内定时器（排在两个月之后的提醒就够了）。
/// 前两处在 <c>BackgroundService.ExecuteAsync</c> 里，异常不是取消，默认处置是停掉整个宿主；第三处被吞成一条错误日志，
/// 工作项丢掉，要等派发恢复过了宽限期才发出去。所以按天切片等待，每段之后按目标时刻重算剩余时长。
/// </para>
/// </remarks>
internal static class LongDelay
{
    /// <summary>单次 <c>Task.Delay</c> 的上限。</summary>
    internal static readonly TimeSpan MaxSlice = TimeSpan.FromDays(1);

    /// <summary>等 <paramref name="delay"/> 这么久；零或负数立刻返回。</summary>
    public static async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay <= TimeSpan.Zero)
            return;

        var now = DateTime.UtcNow;
        var target = delay < DateTime.MaxValue - now ? now + delay : DateTime.MaxValue;

        while (true)
        {
            var remaining = target - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                return;

            await Task.Delay(remaining < MaxSlice ? remaining : MaxSlice, cancellationToken);
        }
    }
}
