namespace Tnzi.Notification.Services;

/// <summary>
/// 投递节奏器：把每个发送账号（渠道 + 服务商键）上的投递摊到每分钟不超过
/// <see cref="DispatchOptions.RatePerMinute"/> 次。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <b>它防的不是「发得快」，是发送账号被服务商的滥用防护封停</b>：连同密码重置这类事务消息一起停摆。
/// 所以它管的是<b>每一次投递</b>（每个收件人），而且整个进程共用一份（模块注册为单例）：一次群发、一次重发失败项、
/// 一次派发恢复的续发，打到的是同一个账号。此前这一项只在派发恢复里每条<b>消息</b>前等一下，正常的发送路径一次都不问它。
/// </para>
/// <para>
/// ★ <b>按渠道 + 服务商键各算各的</b>：服务商的配额按账号计，邮件与短信、默认发送器与具名发送器是不同的账号，
/// 没有理由让短信排在一次邮件群发后面。
/// </para>
/// <para>
/// ★ <b>预订而不是睡完再看</b>：每次投递先领一个时间槽（下一个空槽，至少相隔一个间隔），再等到那一刻。
/// 并发的发送各自领下一个槽，一次群发每次只领一个，所以插进来的事务消息最多等一两个间隔，不会排到整场群发之后。
/// 间隔每次按当前配置算，设置中心里改了就从下一次投递起生效。<c>0</c> = 不限速（默认）。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "Public so that NotificationService can take the process-wide instance from DI; the shape may change if pacing gains per-provider limits.")]
public sealed class NotificationSendPacer
{
    private readonly IOptionsMonitor<NotificationOptions> _options;
    private readonly TimeProvider _clock;
    private readonly Dictionary<(NotificationType Channel, string? ProviderKey), DateTimeOffset> _nextSlot = new();
    private readonly object _gate = new();

    /// <summary>
    /// 初始化一个节奏器。
    /// </summary>
    /// <param name="options">通知配置；每次预订时读 <see cref="DispatchOptions.RatePerMinute"/> 的当前值。</param>
    /// <param name="timeProvider">时钟；不传用系统时钟。</param>
    public NotificationSendPacer(IOptionsMonitor<NotificationOptions> options, TimeProvider? timeProvider = null)
    {
        _options = Check.NotNull(options);
        _clock = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// 为下一次投递预订一个时间槽，返回还要等多久（不限速或槽已经到了时为 <see cref="TimeSpan.Zero"/>）。
    /// </summary>
    /// <param name="channel">投递渠道。</param>
    /// <param name="providerKey">服务商键；<see langword="null"/> = 该渠道的默认发送器。</param>
    public TimeSpan Reserve(NotificationType channel, string? providerKey)
    {
        var ratePerMinute = _options.CurrentValue.Dispatch.RatePerMinute;
        if (ratePerMinute <= 0)
            return TimeSpan.Zero;

        var interval = TimeSpan.FromMilliseconds(60_000d / ratePerMinute);
        var now = _clock.GetUtcNow();
        var key = (channel, NotificationProviderKeys.Normalize(providerKey));

        lock (_gate)
        {
            var slot = _nextSlot.TryGetValue(key, out var next) && next > now ? next : now;
            _nextSlot[key] = slot + interval;
            return slot - now;
        }
    }
}
