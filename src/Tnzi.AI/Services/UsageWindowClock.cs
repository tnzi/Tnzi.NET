namespace Tnzi.AI.Services;

/// <summary>
/// 用量窗口的时间规则（纯计算，不碰库）。语义见 <see cref="UsageLimitWindow"/>。
/// </summary>
internal static class UsageWindowClock
{
    /// <summary>
    /// 某一时刻的窗口：开始时间与窗口里的用量。窗口已经过期的，按「已重置」给出。
    /// </summary>
    /// <param name="window">哪个窗口。</param>
    /// <param name="startedAt">库里记的开始时间。</param>
    /// <param name="used">库里记的用量。</param>
    /// <param name="duration">窗口长度。</param>
    /// <param name="now">当前时刻（UTC）。</param>
    /// <returns>
    /// 5 小时窗口过期后开始时间为 null（下一次使用重新开）；每周窗口过期后滚到包含 <paramref name="now"/> 的那个周期，
    /// 开始时间仍在（周期首尾相接）。两者用量都归零。
    /// </returns>
    public static (DateTime? StartedAt, decimal Used) Current(
        UsageLimitWindow window, DateTime? startedAt, decimal used, TimeSpan duration, DateTime now)
    {
        if (startedAt is not { } start)
        {
            return (null, 0m);
        }

        // 开始时间在将来（时钟回拨）当作仍在这个窗口里，不凭一个说不清的时间清掉用量
        if (now < start + duration)
        {
            return (start, used);
        }

        return window == UsageLimitWindow.Session
            ? (null, 0m)
            : (RollWeekly(start, duration, now), 0m);
    }

    /// <summary>每周窗口过期后包含 <paramref name="now"/> 的那个周期的开始时间。</summary>
    public static DateTime RollWeekly(DateTime start, TimeSpan duration, DateTime now)
    {
        var periods = (now - start).Ticks / duration.Ticks;
        return start.AddTicks(periods * duration.Ticks);
    }

    /// <summary>一个窗口在某一时刻的完整状态（管理端与用户端的用量都由它投影）。</summary>
    public static UsageWindowStatusDto Status(
        UsageLimitWindow window, DateTime? storedStart, decimal storedUsed, TimeSpan duration,
        decimal? limitUsd, decimal warningThreshold, DateTime now)
    {
        var (startedAt, used) = Current(window, storedStart, storedUsed, duration, now);
        var exceeded = limitUsd is { } cap && used >= cap;

        return new UsageWindowStatusDto
        {
            Window = window,
            DurationMinutes = (int)Math.Min(int.MaxValue, Math.Round(duration.TotalMinutes)),
            LimitUsd = limitUsd,
            UsedUsd = used,
            UsedPercent = limitUsd switch
            {
                null => null,
                0m => 100m,
                { } limit => Math.Round(used / limit * 100m, 2)
            },
            StartedAt = startedAt,
            ResetsAt = startedAt is { } start ? start + duration : null,
            Exceeded = exceeded,
            WarningReached = exceeded || (limitUsd is { } positive and > 0m && used >= positive * warningThreshold)
        };
    }
}
