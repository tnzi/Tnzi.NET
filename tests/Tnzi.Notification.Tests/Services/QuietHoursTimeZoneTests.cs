namespace Tnzi.Notification.Tests.Services;

/// <summary>
/// 静默时段按偏好上的时区解释：平常的夜晚、夏令时切换的那两夜、以及落在缺口与重叠里的边界时刻。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <b>为什么要时区而不是让写入方换算成 UTC</b>：固定换算在每一次夏令时切换后都偏一个小时，
/// 而且毫无症状。下面「春季拨快那一夜」那条就是证据：冬天按 -5 换算好的 UTC 窗口，
/// 在三月第二个周日之后会把消息多压一个小时。
/// </para>
/// <para>
/// 日期都在 2026 年：America/Toronto 在 3 月 8 日 02:00 EST 拨到 03:00 EDT（07:00 UTC），
/// 在 11 月 1 日 02:00 EDT 拨回 01:00 EST（06:00 UTC）。Asia/Tokyo 没有夏令时，用来验跨日。
/// </para>
/// </remarks>
public class QuietHoursTimeZoneTests
{
    private static readonly TimeZoneInfo Toronto = TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");
    private static readonly TimeZoneInfo Tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");

    private static DateTime Utc(int month, int day, int hour, int minute = 0)
        => new(2026, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static TimeOnly T(int hour, int minute = 0) => new(hour, minute);

    private static QuietHoursWindow Window(TimeOnly start, TimeOnly end, TimeZoneInfo zone) => new(start, end, zone);

    // ── 平常的夜晚 ───────────────────────────────────────────────────────────

    /// <summary>夏天：本地 22:00–07:00 = UTC 02:00–11:00。</summary>
    [Fact]
    public void A_summer_night_ends_at_seven_local()
        => Window(T(22), T(7), Toronto).EndsAfter(Utc(7, 15, 3)).ShouldBe(Utc(7, 15, 11));

    /// <summary>冬天：同一个本地窗口 = UTC 03:00–12:00。同一行偏好，两个季节两个答案。</summary>
    [Fact]
    public void A_winter_night_ends_at_seven_local()
        => Window(T(22), T(7), Toronto).EndsAfter(Utc(1, 15, 4)).ShouldBe(Utc(1, 15, 12));

    /// <summary>
    /// ★ 时区真的参与了判定：UTC 23:00 落在 UTC 解释的 22:00–07:00 里，
    /// 而它在多伦多是傍晚 19:00，不在本地窗口里。
    /// </summary>
    [Fact]
    public void The_same_wall_times_mean_different_instants_in_different_zones()
    {
        var evening = Utc(7, 15, 23);

        Window(T(22), T(7), Toronto).Contains(evening).ShouldBeFalse();
        new QuietHoursWindow(T(22), T(7)).Contains(evening).ShouldBeTrue("control: a zone-less window is UTC");
    }

    /// <summary>东边的时区跨午夜：本地日期比 UTC 早一天开始，结束时刻仍是本地第二天 07:00。</summary>
    [Theory]
    [InlineData(14)]  // 东京 23:00（7 月 15 日）
    [InlineData(20)]  // 东京 05:00（7 月 16 日）
    public void A_window_crossing_local_midnight_ends_the_next_local_morning(int utcHour)
        => Window(T(22), T(7), Tokyo).EndsAfter(Utc(7, 15, utcHour)).ShouldBe(Utc(7, 15, 22));

    [Fact]
    public void A_moment_before_the_local_start_is_outside()
        => Window(T(22), T(7), Tokyo).Contains(Utc(7, 15, 12)).ShouldBeFalse(); // 东京 21:00

    // ── 夏令时切换的那两夜 ───────────────────────────────────────────────────

    /// <summary>
    /// ★★★ 春季拨快那一夜：窗口在 EST 开始、在 EDT 结束，只有 8 个小时。
    /// 冬天按 -5 换算出的固定 UTC 窗口会压到 12:00 UTC（本地 08:00），多压一个小时，且没有任何症状。
    /// </summary>
    [Fact]
    public void Spring_forward_night_ends_at_seven_on_the_new_offset()
    {
        var window = Window(T(22), T(7), Toronto);

        window.Contains(Utc(3, 8, 2, 59)).ShouldBeFalse(); // 3 月 7 日 21:59 EST
        window.Contains(Utc(3, 8, 3)).ShouldBeTrue();      // 3 月 7 日 22:00 EST
        window.EndsAfter(Utc(3, 8, 5)).ShouldBe(Utc(3, 8, 11)); // 07:00 EDT
    }

    /// <summary>秋季拨慢那一夜：窗口在 EDT 开始、在 EST 结束，有 10 个小时。</summary>
    [Fact]
    public void Fall_back_night_ends_at_seven_on_the_new_offset()
    {
        var window = Window(T(22), T(7), Toronto);

        window.Contains(Utc(11, 1, 1, 59)).ShouldBeFalse(); // 10 月 31 日 21:59 EDT
        window.Contains(Utc(11, 1, 2)).ShouldBeTrue();      // 10 月 31 日 22:00 EDT
        window.EndsAfter(Utc(11, 1, 4)).ShouldBe(Utc(11, 1, 12)); // 07:00 EST
    }

    // ── 边界时刻落在缺口里（那一天不存在的本地时刻）────────────────────────────

    /// <summary>
    /// ★★ 结束时刻 02:30 那天不存在：时钟从 01:59 EST 直接跳到 03:00 EDT，跳的那一瞬就已经过了 02:30。
    /// 结束取跳变那一刻（07:00 UTC），不是往后再顺延一段缺口长度。
    /// </summary>
    [Fact]
    public void An_end_inside_the_spring_gap_is_the_moment_the_clock_jumps()
    {
        var window = Window(T(22), T(2, 30), Toronto);

        window.EndsAfter(Utc(3, 8, 6, 30)).ShouldBe(Utc(3, 8, 7)); // 01:30 EST 时问
        window.Contains(Utc(3, 8, 6, 59)).ShouldBeTrue();          // 01:59 EST
        window.Contains(Utc(3, 8, 7)).ShouldBeFalse();             // 03:00 EDT
    }

    /// <summary>开始时刻 02:30 那天不存在：窗口从跳变那一刻开始。</summary>
    [Fact]
    public void A_start_inside_the_spring_gap_is_the_moment_the_clock_jumps()
    {
        var window = Window(T(2, 30), T(6), Toronto);

        window.Contains(Utc(3, 8, 6, 59)).ShouldBeFalse();          // 01:59 EST
        window.Contains(Utc(3, 8, 7)).ShouldBeTrue();               // 03:00 EDT
        window.EndsAfter(Utc(3, 8, 7, 15)).ShouldBe(Utc(3, 8, 10)); // 06:00 EDT
    }

    // ── 边界时刻落在重叠里（那一天出现两次的本地时刻）──────────────────────────

    /// <summary>
    /// ★★ 结束时刻 01:30 那天出现两次：窗口在时钟<b>第一次</b>走到 01:30 时结束，
    /// 而且不会在重复的那一小时里重新打开。
    /// </summary>
    [Fact]
    public void An_end_inside_the_fall_overlap_is_its_first_occurrence()
    {
        var window = Window(T(22), T(1, 30), Toronto);

        window.EndsAfter(Utc(11, 1, 5, 10)).ShouldBe(Utc(11, 1, 5, 30)); // 01:10 EDT 时问，01:30 EDT 结束
        window.Contains(Utc(11, 1, 6, 10)).ShouldBeFalse();              // 第二遍的 01:10 EST
    }

    /// <summary>开始时刻 01:30 出现两次：从第一次开始，重复的那一小时都算在窗口里。</summary>
    [Fact]
    public void A_start_inside_the_fall_overlap_is_its_first_occurrence()
    {
        var window = Window(T(1, 30), T(6), Toronto);

        window.Contains(Utc(11, 1, 5, 15)).ShouldBeFalse();              // 01:15 EDT，还没开始
        window.Contains(Utc(11, 1, 5, 45)).ShouldBeTrue();               // 01:45 EDT
        window.Contains(Utc(11, 1, 6, 15)).ShouldBeTrue();               // 01:15 EST（重复的那一小时）
        window.EndsAfter(Utc(11, 1, 5, 45)).ShouldBe(Utc(11, 1, 11));    // 06:00 EST
    }

    /// <summary>
    /// 结束时刻那一刻不一定就出了免打扰：下一次窗口可能正好从这一刻开始。多伦多 03:00 至次日 02:30，
    /// 2026-03-08 那天 02:30 不存在，这一次结束在跳变的 03:00 EDT（07:00 UTC），下一次也从 03:00 开始。
    /// 延后到那一刻的人会再被延后一次，而且仍严格晚于当下。
    /// </summary>
    [Fact]
    public void An_end_can_coincide_with_the_next_start()
    {
        var window = Window(T(3), T(2, 30), Toronto);
        var end = window.EndsAfter(Utc(3, 8, 6, 30)); // 01:30 EST

        end.ShouldBe(Utc(3, 8, 7));
        window.Contains(end!.Value).ShouldBeTrue("the next window starts at the same instant");
        window.EndsAfter(end.Value)!.Value.ShouldBeGreaterThan(end.Value);
    }

    /// <summary>
    /// ⚠️ 只认 UTC 的老读取口装不下长于 24 小时的窗口：多伦多 22:00 至次日 21:59 在秋季拨慢那一次长
    /// 24 小时 59 分，折成一天里的两个 UTC 时刻只剩 (02:00, 02:59)。接口文档写着这条限制，这里把它钉住。
    /// </summary>
    [Fact]
    public void The_utc_only_shape_collapses_a_day_long_window_on_the_fall_back_night()
        => Window(T(22), T(21, 59), Toronto).ToUtcTimesOfDay(Utc(11, 1, 12))
            .ShouldBe((T(2), T(2, 59)));

    // ── 还没跟上时区的替换实现 ───────────────────────────────────────────────

    /// <summary>
    /// ★ 消费方自己实现了偏好服务、只实现了老的 UTC 批量口：发送路径改问带时区的那个口之后，
    /// 默认接口实现把老口的结果包成 UTC 窗口，它的静默时段照常生效，而不是被安静地当成「没人设」。
    /// </summary>
    [Fact]
    public async Task A_replacement_service_written_before_time_zones_still_feeds_the_send_path()
    {
        var user = Guid.NewGuid();
        INotificationPreferenceService legacy = new UtcOnlyPreferences(user, T(22), T(7));

        var windows = await legacy.GetQuietHoursWindowsAsync([user], NotificationType.Email);

        windows.ShouldContainKey(user);
        windows[user].Contains(new DateTime(2026, 7, 15, 23, 0, 0, DateTimeKind.Utc)).ShouldBeTrue();
        windows[user].Contains(new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc)).ShouldBeFalse();
    }

    /// <summary>只实现了引入时区之前那份接口的替换实现（其余成员与本用例无关）。</summary>
    private sealed class UtcOnlyPreferences(Guid user, TimeOnly start, TimeOnly end) : INotificationPreferenceService
    {
        public Task<IReadOnlyDictionary<Guid, (TimeOnly Start, TimeOnly End)>> GetQuietHoursAsync(
            IEnumerable<Guid> userIds, NotificationType channel, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, (TimeOnly Start, TimeOnly End)>>(
                new Dictionary<Guid, (TimeOnly Start, TimeOnly End)> { [user] = (start, end) });

        public Task<Result<IPagedList<NotificationPreferenceDto>>> GetPagedListAsync(NotificationPreferenceQueryDto query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<List<NotificationPreferenceDto>>> GetUserPreferencesAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<NotificationPreferenceDto>> SetPreferenceAsync(Guid userId, SetNotificationPreferenceDto input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> DeletePreferenceAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> ResetToDefaultAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> IsChannelEnabledAsync(Guid userId, string channel, string? category = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<Guid>> FilterEnabledUsersAsync(IEnumerable<Guid> userIds, NotificationType channel, string? category = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> IsInQuietHoursAsync(Guid userId, string channel, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, int>> GetFrequencyCapsAsync(IEnumerable<Guid> userIds, NotificationType channel, string? category = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    // ── 对所有时刻都要成立的那几条 ───────────────────────────────────────────

    public static TheoryData<string> Zones => new() { "UTC", "America/Toronto", "Europe/London", "Australia/Lord_Howe", "Asia/Tokyo" };

    /// <summary>
    /// ★★★ 扫过 2026 年各时区夏令时切换前后的每五分钟：<b>延后的结束时刻严格晚于当下</b>
    /// （否则恢复扫描立刻再取、再延后，一个不发消息也不报错的循环）、两个方法彼此一致、
    /// 结束前一刻仍在窗口里，且一次延后不超过一个窗口的长度加一次切换。
    /// </summary>
    [Theory]
    [MemberData(nameof(Zones))]
    public void Every_deferral_ends_strictly_after_now_and_agrees_with_contains(string zoneId)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var windows = new[]
        {
            Window(T(22), T(7), zone),
            Window(T(22), T(2, 30), zone),
            Window(T(2, 30), T(6), zone),
            Window(T(22), T(1, 30), zone),
            Window(T(1, 30), T(6), zone),
            Window(T(3), T(2, 30), zone),
            Window(T(9), T(17), zone),
        };
        var weekends = new[] { Utc(3, 6, 0), Utc(3, 27, 0), Utc(4, 3, 0), Utc(10, 2, 0), Utc(10, 23, 0), Utc(10, 30, 0) };

        foreach (var window in windows)
        {
            foreach (var from in weekends)
            {
                for (var now = from; now < from.AddDays(3); now = now.AddMinutes(5))
                {
                    var ends = window.EndsAfter(now);

                    window.Contains(now).ShouldBe(ends.HasValue, $"{window} at {now:u}");
                    if (ends is not { } end)
                        continue;

                    end.ShouldBeGreaterThan(now, $"{window} at {now:u}");
                    end.Kind.ShouldBe(DateTimeKind.Utc);
                    (end - now).ShouldBeLessThanOrEqualTo(TimeSpan.FromHours(25), $"{window} at {now:u}");
                    window.Contains(end.AddTicks(-1)).ShouldBeTrue($"{window}: the moment before {end:u} is still quiet");
                }
            }
        }
    }
}
