namespace Tnzi.Tests.Utilities;

/// <summary>
/// DateTimeRange 静态预设的 Kind 一致性守卫。
///
/// 背景：预设按设计产出**本地时间**边界，消费方用 <see cref="DateTimeRangeExtensions.ToUtc"/>
/// 换算后再查库。而 <c>ToUtc()</c> 只对 <see cref="DateTimeKind.Local"/> 做真实换算，
/// 对 <see cref="DateTimeKind.Unspecified"/> 只改标签 —— 于是任何漏写 Kind 的预设
/// （<c>new DateTime(y, m, d)</c> 默认就是 Unspecified）都会在非 UTC 时区把边界整体偏移，
/// 且**不报任何错**。这条守卫把「所有预设必须是 Local」钉死。
/// </summary>
public class DateTimeRangeTests
{
    public static TheoryData<string, DateTimeRange> AllPresets() => new()
    {
        { nameof(DateTimeRange.Yesterday), DateTimeRange.Yesterday },
        { nameof(DateTimeRange.Today), DateTimeRange.Today },
        { nameof(DateTimeRange.Tomorrow), DateTimeRange.Tomorrow },
        { nameof(DateTimeRange.ThisWeek), DateTimeRange.ThisWeek },
        { nameof(DateTimeRange.LastWeek), DateTimeRange.LastWeek },
        { nameof(DateTimeRange.ThisMonth), DateTimeRange.ThisMonth },
        { nameof(DateTimeRange.LastMonth), DateTimeRange.LastMonth },
        { nameof(DateTimeRange.ThisYear), DateTimeRange.ThisYear },
        { nameof(DateTimeRange.Last7Days), DateTimeRange.Last7Days },
        { nameof(DateTimeRange.Last30Days), DateTimeRange.Last30Days }
    };

    [Theory]
    [MemberData(nameof(AllPresets))]
    public void Presets_AreLocalKind(string name, DateTimeRange range)
    {
        Assert.Equal(DateTimeKind.Local, range.StartTime.Kind);
        Assert.Equal(DateTimeKind.Local, range.EndTime.Kind);
        Assert.True(range.StartTime <= range.EndTime, $"{name}: StartTime must not exceed EndTime.");
    }

    [Theory]
    [MemberData(nameof(AllPresets))]
    public void Presets_ToUtc_ConvertsInstantRatherThanRelabelling(string name, DateTimeRange range)
    {
        var utc = range.ToUtc();

        Assert.Equal(DateTimeKind.Utc, utc.StartTime.Kind);
        Assert.Equal(DateTimeKind.Utc, utc.EndTime.Kind);

        // 换算而非改标签：UTC 边界应等于本地边界的 ToUniversalTime()。
        // Unspecified 的预设走的是"只改标签"分支，这里会红。
        Assert.Equal(range.StartTime.ToUniversalTime(), utc.StartTime);
        Assert.Equal(range.EndTime.ToUniversalTime(), utc.EndTime);
        Assert.True(utc.StartTime <= utc.EndTime, $"{name}: UTC bounds must stay ordered.");

        // 刻意不断言「跨度不变」：横跨夏令时切换的月份换算到 UTC 后就是会差一小时，
        // 那是正确行为（那个月本来就少/多一小时），断言不变会在 3 月和 11 月假红。
    }

    [Fact]
    public void ThisMonth_CoversWholeCalendarMonth()
    {
        var now = DateTime.Now;
        var range = DateTimeRange.ThisMonth;

        Assert.Equal(new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Local), range.StartTime);
        Assert.Equal(now.Month, range.EndTime.Month);
        Assert.True(range.Contains(now));
    }

    [Fact]
    public void ThisWeek_StartsOnMonday()
    {
        Assert.Equal(DayOfWeek.Monday, DateTimeRange.ThisWeek.StartTime.DayOfWeek);
    }

    [Fact]
    public void LastMonth_EndsImmediatelyBeforeThisMonthStarts()
    {
        var lastMonth = DateTimeRange.LastMonth;
        var thisMonth = DateTimeRange.ThisMonth;

        Assert.True(lastMonth.EndTime < thisMonth.StartTime);
        Assert.False(lastMonth.Overlaps(new DateTimeRange(thisMonth.StartTime, thisMonth.EndTime)));
    }
}
