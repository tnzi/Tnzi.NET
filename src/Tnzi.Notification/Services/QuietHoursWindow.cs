namespace Tnzi.Notification.Services;

/// <summary>
/// 一个人的静默时段：一天里的两个<b>墙上时刻</b>，按 <see cref="Zone"/> 解释；
/// 以及「从某一刻算，它什么时候结束」。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>跨午夜是常态而不是边界情形</b>：绝大多数人设的就是 22:00–06:00。
/// <c>Start &gt; End</c> 即表示跨过午夜，窗口的结束时刻落在<b>第二天</b>。
/// </para>
/// <para>
/// ★★ <b>结束时刻必须算成一个绝对时刻（UTC），不能只存一个 <c>TimeOnly</c>。</b>
/// 收件人是被<b>延后</b>而不是被丢弃，所以库里要写下「什么时候再发」：
/// 只写「06:00」的话，恢复扫描无从判断那是今天的还是明天的六点。
/// </para>
/// <para>
/// ★★ <b>按时区解释，而不是让写入方换算成 UTC。</b>固定的 UTC 换算在每一次夏令时切换后
/// 都偏一个小时，而且毫无症状：设了本地 22:00 的人，半年在 21:00、半年在 23:00 进入静默。
/// 没有时区（<see cref="Zone"/> 为 UTC）时两个时刻就是 UTC 时刻，与引入时区之前逐字一致。
/// </para>
/// <para>
/// ★ <b>窗口是半开区间 [开始, 结束)</b>：到了结束时刻就已经可以发。闭区间会让「恰好在
/// 结束那一刻」既算在窗口里、又没有任何严格晚于当下的结束时刻可写，只能延后到明天。
/// </para>
/// <para>
/// ★ <b>夏令时切换按同一条规则处理：一个墙上时刻 = 本地时钟当天<b>第一次</b>走到它的那一瞬。</b>
/// <list type="bullet">
/// <item><description>春季拨快造成的缺口（那个时刻当天根本不存在）：取时钟跳过它的那一瞬，
/// 即缺口结束处。设了 02:30 结束的人，在 02:00 直接跳到 03:00 的那一刻就已经过了 02:30。</description></item>
/// <item><description>秋季拨慢造成的重叠（那个时刻出现两次）：取较早的那一次。
/// 窗口在时钟第一次显示结束时刻时结束，不会在重复的那一小时里重新打开。</description></item>
/// </list>
/// 两条都是同一句话的推论。<see cref="EndsAfter"/> 返回的是<b>包含当下的那一次</b>窗口的结束时刻，
/// 在那之前 <see cref="Contains"/> 一直为 <see langword="true"/>；但到了那一刻 <see cref="Contains"/>
/// <b>不一定</b>变成 <see langword="false"/>：下一次窗口可能恰好在这一刻开始。例如多伦多
/// 03:00 至次日 02:30，在 2026-03-08 那天 02:30 不存在，这一次窗口结束在跳变的 03:00 EDT，
/// 而下一次窗口正好从 03:00 开始。延后到那一刻的收件人会被再延后一次，这是正确的（他仍在免打扰里），
/// 而「严格晚于当下」保证了它不会原地打转。
/// </para>
/// </remarks>
/// <param name="Start">静默开始（墙上时刻）。</param>
/// <param name="End">静默结束（墙上时刻，不含）。</param>
/// <param name="Zone">解释这两个时刻的时区；<see langword="null"/> = UTC。</param>
[ExperimentalApi(Reason = "Public since quiet hours became time-zone aware; the shape may still change while the self-service UI settles.")]
public readonly record struct QuietHoursWindow(TimeOnly Start, TimeOnly End, TimeZoneInfo? Zone = null)
{
    /// <summary>缺口最长按两天找（历史上整天跳过的时区也在范围内），找不到就退回基准偏移。</summary>
    private const int MaxGapSearchMinutes = 48 * 60;

    /// <summary>这一刻在不在静默时段内。</summary>
    /// <param name="utcNow">判定时刻（UTC）。</param>
    public bool Contains(DateTime utcNow) => OccurrenceContaining(utcNow) is not null;

    /// <summary>
    /// 从 <paramref name="utcNow"/> 算，这个静默时段<b>结束</b>的绝对时刻（UTC）。
    /// 不在窗口内时返回 <see langword="null"/>。
    /// </summary>
    /// <remarks>
    /// ★ 结果保证<b>严格晚于</b> <paramref name="utcNow"/>：等于或早于当前时刻的「延后」
    /// 会让恢复扫描立刻再取一次，而那一次又会再延后一次，成了一个不发消息也不报错的循环。
    /// 半开区间让这一条由构造保证：「在窗口里」本身就意味着 <c>utcNow &lt; 结束</c>。
    /// </remarks>
    /// <param name="utcNow">判定时刻（UTC）。</param>
    public DateTime? EndsAfter(DateTime utcNow) => OccurrenceContaining(utcNow)?.End;

    /// <summary>
    /// 包含 <paramref name="utcNow"/> 的那一次，或者紧随其后的下一次，换算成 UTC 的一天内时刻。
    /// </summary>
    /// <remarks>
    /// 只服务于 <see cref="INotificationPreferenceService.GetQuietHoursAsync"/> 那个只认 UTC 时刻的老契约：
    /// 换算按「这一次」各自的偏移，所以当天正确；跨过下一次夏令时切换就不再正确；
    /// 要精确判定请直接用 <see cref="Contains"/> / <see cref="EndsAfter"/>。
    /// </remarks>
    internal (TimeOnly Start, TimeOnly End) ToUtcTimesOfDay(DateTime utcNow)
    {
        var occurrence = OccurrenceContaining(utcNow) ?? NextOccurrence(utcNow);
        return (TimeOnly.FromDateTime(occurrence.Start), TimeOnly.FromDateTime(occurrence.End));
    }

    private TimeZoneInfo EffectiveZone => Zone ?? TimeZoneInfo.Utc;

    private (DateTime Start, DateTime End)? OccurrenceContaining(DateTime utcNow)
    {
        var today = LocalDateOf(utcNow);

        // 跨午夜窗口在凌晨那一半时，包含「现在」的是昨晚开始的那一次；两次都看。
        return IfContains(OccurrenceStartingOn(today), utcNow)
            ?? IfContains(OccurrenceStartingOn(today.AddDays(-1)), utcNow);
    }

    private static (DateTime Start, DateTime End)? IfContains((DateTime Start, DateTime End) occurrence, DateTime utcNow)
        => utcNow >= occurrence.Start && utcNow < occurrence.End ? occurrence : null;

    private (DateTime Start, DateTime End) NextOccurrence(DateTime utcNow)
    {
        var today = LocalDateOf(utcNow);
        var occurrence = OccurrenceStartingOn(today);
        return occurrence.Start > utcNow ? occurrence : OccurrenceStartingOn(today.AddDays(1));
    }

    /// <summary>在本地日期 <paramref name="anchor"/> 开始的那一次窗口（UTC 起止）。</summary>
    /// <remarks><c>Start == End</c> 得到一个空区间，永远不包含任何时刻：退化配置不延后任何人。</remarks>
    private (DateTime Start, DateTime End) OccurrenceStartingOn(DateOnly anchor)
    {
        var start = FirstReach(anchor, Start);
        var end = FirstReach(Start <= End ? anchor : anchor.AddDays(1), End);
        return (start, end);
    }

    private DateOnly LocalDateOf(DateTime utcNow)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow, EffectiveZone));

    /// <summary>本地时钟在 <paramref name="date"/> 这一天第一次走到 <paramref name="time"/> 的那一瞬（UTC）。</summary>
    private DateTime FirstReach(DateOnly date, TimeOnly time)
    {
        var zone = EffectiveZone;
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);

        if (zone.IsInvalidTime(local))
            return EndOfGap(zone, local);

        if (zone.IsAmbiguousTime(local))
        {
            // 重叠：两个偏移里较大的那个对应较早的瞬间。
            // ★ ConvertTimeToUtc 对重叠时刻取的是标准时间（较晚那次），不能直接用。
            var earlier = zone.GetAmbiguousTimeOffsets(local).Max();
            return DateTime.SpecifyKind(local - earlier, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    /// <summary>
    /// <paramref name="local"/> 落在缺口里：返回时钟跳过缺口的那一瞬，也就是缺口后第一个存在的本地时刻。
    /// </summary>
    /// <remarks>
    /// ★ <see cref="TimeZoneInfo.ConvertTimeToUtc(DateTime, TimeZoneInfo)"/> 对缺口里的时刻直接抛异常。
    /// 按分钟向后找：切换在现实时区数据里都落在整分钟上，所以从整分钟起步找到的就是缺口终点。
    /// </remarks>
    private static DateTime EndOfGap(TimeZoneInfo zone, DateTime local)
    {
        var probe = new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, DateTimeKind.Unspecified);

        for (var i = 0; i < MaxGapSearchMinutes && zone.IsInvalidTime(probe); i++)
            probe = probe.AddMinutes(1);

        return zone.IsInvalidTime(probe)
            ? DateTime.SpecifyKind(local - zone.BaseUtcOffset, DateTimeKind.Utc)
            : TimeZoneInfo.ConvertTimeToUtc(probe, zone);
    }
}
