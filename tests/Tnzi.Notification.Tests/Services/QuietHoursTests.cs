using Tnzi.Notification.Metadata;

namespace Tnzi.Notification.Tests.Services;

/// <summary>
/// 静默时段的两件事：窗口本身怎么算，以及谁会被延后。
/// </summary>
/// <remarks>
/// ★ 本组是纯函数。<b>它单独不能证明这条链接上了</b> —— 本模块已经两次栽在
/// 「一个考究、正确、无人问津的过滤器」上（退订 2026-08-08、传真渠道 2026-08-20）。
/// 接上的证据在 <c>Integration/QuietHoursSendPathTests</c>。
/// </remarks>
public class QuietHoursTests
{
    private static DateTime Utc(int hour, int minute = 0)
        => new(2026, 9, 4, hour, minute, 0, DateTimeKind.Utc);

    private static QuietHoursWindow Window(int startHour, int endHour)
        => new(new TimeOnly(startHour, 0), new TimeOnly(endHour, 0));

    // ── 窗口本身 ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(2, true)]    // 窗口内
    [InlineData(23, true)]   // 窗口内（午夜前那一半）
    [InlineData(12, false)]  // 窗口外
    [InlineData(7, false)]   // 刚过结束时刻
    public void A_window_that_wraps_midnight_covers_both_halves(int hour, bool expected)
        => Window(22, 6).Contains(Utc(hour)).ShouldBe(expected);

    [Theory]
    [InlineData(3, true)]
    [InlineData(0, false)]
    [InlineData(6, false)]
    public void A_window_inside_one_day_covers_only_that_stretch(int hour, bool expected)
        => Window(1, 5).Contains(Utc(hour)).ShouldBe(expected);

    /// <summary>★ 跨午夜窗口的前半段，结束时刻落在<b>明天</b>。</summary>
    [Fact]
    public void The_end_of_a_wrapping_window_can_fall_on_the_next_day()
        => Window(22, 6).EndsAfter(Utc(23)).ShouldBe(Utc(6).AddDays(1));

    /// <summary>跨午夜窗口的后半段，结束时刻还在今天。</summary>
    [Fact]
    public void The_end_of_a_wrapping_window_is_today_when_midnight_has_passed()
        => Window(22, 6).EndsAfter(Utc(2)).ShouldBe(Utc(6));

    [Fact]
    public void The_end_of_an_ordinary_window_is_today()
        => Window(1, 5).EndsAfter(Utc(3)).ShouldBe(Utc(5));

    /// <summary>不在窗口里就没有「延后到什么时候」可言。</summary>
    [Fact]
    public void A_moment_outside_the_window_has_no_deferral()
        => Window(22, 6).EndsAfter(Utc(12)).ShouldBeNull();

    /// <summary>
    /// ★★ 结束时刻必须<b>严格晚于</b>当下。等于或早于的话，到期扫描会立刻再取一次、
    /// 再延后一次 —— 一个不发消息也不报错的循环。退化配置正好落在这一格上。
    /// </summary>
    [Fact]
    public void A_degenerate_window_never_defers_into_the_past()
    {
        var now = Utc(3);
        var ends = new QuietHoursWindow(new TimeOnly(3, 0), new TimeOnly(3, 0)).EndsAfter(now);

        (ends == null || ends > now).ShouldBeTrue($"deferring to {ends:u} would loop forever at {now:u}");
    }

    // ── 谁会被延后 ───────────────────────────────────────────────────────────

    /// <summary>窗口里的人被标成延后，而不是取消。</summary>
    [Fact]
    public void A_recipient_inside_the_window_is_deferred_rather_than_cancelled()
    {
        var user = Guid.NewGuid();
        var recipient = new Recipient { UserId = user, Address = "a@example.com", Status = NotificationStatus.Pending };

        var remaining = QuietHoursRecipientFilter.Apply(
            [recipient], Windows(user, Window(22, 6)), Utc(23), out var deferred);

        remaining.ShouldBeEmpty();
        deferred.ShouldHaveSingleItem();
        recipient.Status.ShouldBe(NotificationStatus.Scheduled);
        recipient.DeferredUntil.ShouldBe(Utc(6).AddDays(1));
    }

    /// <summary>★ 延后不写 FailureReason：那一列是失败说明，而这不是一次失败。</summary>
    [Fact]
    public void A_deferred_recipient_carries_no_failure_reason()
    {
        var user = Guid.NewGuid();
        var recipient = new Recipient { UserId = user, Address = "a@example.com", Status = NotificationStatus.Pending };

        QuietHoursRecipientFilter.Apply([recipient], Windows(user, Window(22, 6)), Utc(23), out _);

        recipient.FailureReason.ShouldBeNull();
    }

    /// <summary>窗口外的人原样通过。</summary>
    [Fact]
    public void A_recipient_outside_the_window_is_left_alone()
    {
        var user = Guid.NewGuid();
        var recipient = new Recipient { UserId = user, Address = "a@example.com", Status = NotificationStatus.Pending };

        var remaining = QuietHoursRecipientFilter.Apply(
            [recipient], Windows(user, Window(22, 6)), Utc(12), out var deferred);

        remaining.ShouldHaveSingleItem();
        deferred.ShouldBeEmpty();
        recipient.Status.ShouldBe(NotificationStatus.Pending);
    }

    /// <summary>没有 UserId 的收件人不受影响 —— 静默时段按人设。</summary>
    [Fact]
    public void A_recipient_without_a_user_is_left_alone()
    {
        var recipient = new Recipient { Address = "list@example.com", Status = NotificationStatus.Pending };

        var remaining = QuietHoursRecipientFilter.Apply(
            [recipient], Windows(Guid.NewGuid(), Window(22, 6)), Utc(23), out var deferred);

        remaining.ShouldHaveSingleItem();
        deferred.ShouldBeEmpty();
    }

    /// <summary>★ 事务性消息一律豁免：把营销邮件挪到早上的人，不该凌晨收不到验证码。</summary>
    [Fact]
    public void A_transactional_message_never_consults_quiet_hours()
    {
        var message = new Message { IsTransactional = true };
        var recipients = new List<Recipient> { new() { UserId = Guid.NewGuid(), Address = "a@example.com" } };

        QuietHoursRecipientFilter.ShouldConsultQuietHours(message, recipients).ShouldBeFalse();
    }

    /// <summary>对照：普通消息要查（否则上一条在验一个恒假的条件）。</summary>
    [Fact]
    public void An_ordinary_message_with_a_known_user_does_consult_quiet_hours()
    {
        var message = new Message { IsTransactional = false };
        var recipients = new List<Recipient> { new() { UserId = Guid.NewGuid(), Address = "a@example.com" } };

        QuietHoursRecipientFilter.ShouldConsultQuietHours(message, recipients).ShouldBeTrue();
    }

    /// <summary>一个人都没有 UserId 的批次不必去查偏好表。</summary>
    [Fact]
    public void A_batch_of_bare_addresses_does_not_consult_quiet_hours()
    {
        var message = new Message { IsTransactional = false };
        var recipients = new List<Recipient> { new() { Address = "list@example.com" } };

        QuietHoursRecipientFilter.ShouldConsultQuietHours(message, recipients).ShouldBeFalse();
    }

    private static Dictionary<Guid, QuietHoursWindow> Windows(Guid user, QuietHoursWindow window)
        => new() { [user] = window };
}
