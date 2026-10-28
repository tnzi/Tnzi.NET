namespace Tnzi.Notification.Tests.Services;

/// <summary>
/// <see cref="NotificationSendPacer"/> 的预订规则。限速等待期间心跳照样推进由 <c>InFlightBatchTests</c> 在真库上验证（心跳是一条带条件的更新）。
/// </summary>
public class NotificationSendPacerTests
{
    private readonly DispatchOptions _dispatch = new() { RatePerMinute = 60 };
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 10, 11, 9, 0, 0, TimeSpan.Zero));

    private NotificationSendPacer NewPacer()
    {
        var options = new Mock<IOptionsMonitor<NotificationOptions>>();
        options.Setup(o => o.CurrentValue).Returns(() => new NotificationOptions { Dispatch = _dispatch });
        return new NotificationSendPacer(options.Object, _clock);
    }

    /// <summary>★★ 每分钟 60 次：连着预订的三次分别等 0、1、2 秒。</summary>
    [Fact]
    public void ConsecutiveReservations_AreSpacedByTheInterval()
    {
        var pacer = NewPacer();

        pacer.Reserve(NotificationType.Email, null).ShouldBe(TimeSpan.Zero);
        pacer.Reserve(NotificationType.Email, null).ShouldBe(TimeSpan.FromSeconds(1));
        pacer.Reserve(NotificationType.Email, null).ShouldBe(TimeSpan.FromSeconds(2));
    }

    /// <summary>预订的槽过去之后不再等。</summary>
    [Fact]
    public void AReservationAfterTheSlotsHavePassed_DoesNotWait()
    {
        var pacer = NewPacer();
        pacer.Reserve(NotificationType.Email, null);
        pacer.Reserve(NotificationType.Email, null);

        _clock.Advance(TimeSpan.FromSeconds(5));

        pacer.Reserve(NotificationType.Email, null).ShouldBe(TimeSpan.Zero);
    }

    /// <summary>0 = 不限速（默认）。</summary>
    [Fact]
    public void ZeroMeansNoLimit()
    {
        _dispatch.RatePerMinute = 0;
        var pacer = NewPacer();

        pacer.Reserve(NotificationType.Email, null).ShouldBe(TimeSpan.Zero);
        pacer.Reserve(NotificationType.Email, null).ShouldBe(TimeSpan.Zero);
    }

    /// <summary>★ 发送账号各算各的：邮件群发不让短信排队，具名发送器与默认发送器互不占用。</summary>
    [Fact]
    public void EachChannelAndProvider_HasItsOwnBudget()
    {
        var pacer = NewPacer();
        pacer.Reserve(NotificationType.Email, null);

        pacer.Reserve(NotificationType.Sms, null).ShouldBe(TimeSpan.Zero);
        pacer.Reserve(NotificationType.Email, "marketing").ShouldBe(TimeSpan.Zero);
        pacer.Reserve(NotificationType.Email, null).ShouldBe(TimeSpan.FromSeconds(1));
    }

    /// <summary>服务商键不分大小写：同一个账号不能因为写法不同而拿到两份配额。</summary>
    [Fact]
    public void ProviderKeys_AreComparedInTheirNormalForm()
    {
        var pacer = NewPacer();
        pacer.Reserve(NotificationType.Email, "Marketing");

        pacer.Reserve(NotificationType.Email, "marketing").ShouldBe(TimeSpan.FromSeconds(1));
    }

    /// <summary>设置中心里改了配额，下一次预订就按新的间隔算。</summary>
    [Fact]
    public void ARuntimeChangeOfTheRate_AppliesToTheNextReservation()
    {
        var pacer = NewPacer();
        pacer.Reserve(NotificationType.Email, null);
        _clock.Advance(TimeSpan.FromSeconds(1));

        _dispatch.RatePerMinute = 30;
        pacer.Reserve(NotificationType.Email, null).ShouldBe(TimeSpan.Zero);
        pacer.Reserve(NotificationType.Email, null).ShouldBe(TimeSpan.FromSeconds(2));
    }

    /// <summary>可以手动拨动的时钟。</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
