namespace Tnzi.Notification.Services;

/// <summary>
/// 一个人的静默时段（UTC 的一天内的两个时刻），以及「从某一刻算，它什么时候结束」。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>跨午夜是常态而不是边界情形</b>：绝大多数人设的就是 22:00–06:00。
/// <c>Start &gt; End</c> 即表示跨过午夜，此时「在窗口内」是
/// <c>now &gt;= Start || now &lt;= End</c>，而窗口的结束时刻可能落在<b>明天</b>。
/// </para>
/// <para>
/// ★★ <b>结束时刻必须算成一个绝对时刻，不能只存一个 <c>TimeOnly</c>。</b>
/// 收件人是被<b>延后</b>而不是被丢弃，所以库里要写下「什么时候再发」——
/// 只写「06:00」的话，恢复扫描无从判断那是今天的还是明天的六点。
/// </para>
/// <para>
/// ★ 两个时刻按 UTC 解释，与 <c>Preference.QuietHoursStart</c> / <c>QuietHoursEnd</c> 的
/// 声明一致。本模块<b>不</b>做时区换算：偏好表里没有时区列，猜一个只会让
/// 「设了 22:00 却在 22:00 收到」这种毫无症状的错误变得普遍。消费方要按本地时间设，
/// 应当在写入偏好时换算成 UTC。
/// </para>
/// </remarks>
/// <param name="Start">静默开始（UTC 时刻）。</param>
/// <param name="End">静默结束（UTC 时刻）。</param>
internal readonly record struct QuietHoursWindow(TimeOnly Start, TimeOnly End)
{
    /// <summary>这一刻在不在静默时段内。</summary>
    public bool Contains(DateTime utcNow)
    {
        var now = TimeOnly.FromDateTime(utcNow);

        return Start <= End
            ? now >= Start && now <= End
            : now >= Start || now <= End;   // 跨午夜
    }

    /// <summary>
    /// 从 <paramref name="utcNow"/> 算，这个静默时段<b>结束</b>的绝对时刻。
    /// 不在窗口内时返回 <see langword="null"/>。
    /// </summary>
    /// <remarks>
    /// ★ 结果保证<b>严格晚于</b> <paramref name="utcNow"/>：等于或早于当前时刻的「延后」
    /// 会让恢复扫描立刻再取一次，而那一次又会再延后一次 —— 一个不发消息也不报错的循环。
    /// 退化配置（<c>Start == End</c>）正好落在这一格上，所以判据写在这里而不是调用方。
    /// </remarks>
    public DateTime? EndsAfter(DateTime utcNow)
    {
        if (!Contains(utcNow))
            return null;

        var today = utcNow.Date;
        var endToday = today + End.ToTimeSpan();

        // 今天的结束时刻还没到就是它；已经过了说明我们在跨午夜窗口的前半段，结束在明天。
        var ends = endToday > utcNow ? endToday : endToday.AddDays(1);

        return DateTime.SpecifyKind(ends, DateTimeKind.Utc);
    }
}
