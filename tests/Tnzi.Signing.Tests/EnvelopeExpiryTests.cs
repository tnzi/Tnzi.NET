using Tnzi.Signing.Entities;
using Tnzi.Signing.Metadata;
using Tnzi.Signing.Services.Internal;

namespace Tnzi.Signing.Tests;

/// <summary>
/// 过期判定。这里最要紧的不是某一条判断对不对，而是<b>筛选与展示不会各说各话</b> ——
/// 「按已过期筛选」列出来的行，必须正好就是列表里标着「已过期」的那批。
/// </summary>
public class EnvelopeExpiryTests
{
    private static readonly DateTime Now = new(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc);

    private static Envelope At(EnvelopeStatus status, DateTime expiresAt)
        => new() { Id = Guid.NewGuid(), Status = status, ExpiresAt = expiresAt };

    public static TheoryData<EnvelopeStatus> AllStatuses()
    {
        var data = new TheoryData<EnvelopeStatus>();
        foreach (var s in Enum.GetValues<EnvelopeStatus>())
            data.Add(s);
        return data;
    }

    // ── 派生 ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(EnvelopeStatus.Sent)]
    [InlineData(EnvelopeStatus.InProgress)]
    public void A_request_still_awaiting_signatures_reads_as_expired_once_its_time_is_up(EnvelopeStatus stored)
    {
        var derived = EnvelopeExpiry.Derive(stored, Now.AddSeconds(-1), Now);

        Assert.Equal(EnvelopeStatus.Expired, derived);
    }

    [Theory]
    [InlineData(EnvelopeStatus.Completed)]
    [InlineData(EnvelopeStatus.Declined)]
    [InlineData(EnvelopeStatus.Voided)]
    public void A_settled_request_never_becomes_expired_no_matter_how_long_ago_it_settled(EnvelopeStatus stored)
    {
        // 那份文件的结局已经定了。签成了就是签成了，再过十年也不会变成"过期"。
        var derived = EnvelopeExpiry.Derive(stored, Now.AddYears(-5), Now);

        Assert.Equal(stored, derived);
    }

    [Fact]
    public void A_draft_does_not_expire_because_it_was_never_sent_to_anyone()
    {
        var derived = EnvelopeExpiry.Derive(EnvelopeStatus.Draft, Now.AddDays(-1), Now);

        Assert.Equal(EnvelopeStatus.Draft, derived);
    }

    [Fact]
    public void The_deadline_itself_counts_as_expired()
    {
        // CheckSignable 用的是 `ExpiresAt <= now` 拒签，展示必须用同一个边界 ——
        // 否则会出现"显示还能签、点下去被拒"的那一秒。
        var derived = EnvelopeExpiry.Derive(EnvelopeStatus.Sent, Now, Now);

        Assert.Equal(EnvelopeStatus.Expired, derived);
    }

    [Fact]
    public void A_request_still_within_its_window_keeps_its_stored_status()
    {
        var derived = EnvelopeExpiry.Derive(EnvelopeStatus.Sent, Now.AddSeconds(1), Now);

        Assert.Equal(EnvelopeStatus.Sent, derived);
    }

    // ── 筛选与派生同源（本文件的主张） ──────────────────────────────────────

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public void Everything_a_status_filter_selects_reads_back_as_that_same_status(EnvelopeStatus status)
    {
        // 覆盖每个状态 × 过期/未过期两种时间，跑一遍完整的交叉。
        var universe = Enum.GetValues<EnvelopeStatus>()
            .SelectMany(s => new[] { At(s, Now.AddDays(-1)), At(s, Now.AddDays(1)) })
            .ToList();

        var selected = universe.AsQueryable()
            .Where(EnvelopeExpiry.StatusFilter(status, Now))
            .ToList();

        Assert.NotEmpty(selected);
        foreach (var e in selected)
            Assert.Equal(status, EnvelopeExpiry.Derive(e.Status, e.ExpiresAt, Now));
    }

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public void A_status_filter_leaves_nothing_of_that_status_behind(EnvelopeStatus status)
    {
        // 上一条防"筛多了"，这条防"筛漏了"。两条合起来 = 筛选结果与派生结果是同一个集合。
        var universe = Enum.GetValues<EnvelopeStatus>()
            .SelectMany(s => new[] { At(s, Now.AddDays(-1)), At(s, Now.AddDays(1)) })
            .ToList();

        var selectedIds = universe.AsQueryable()
            .Where(EnvelopeExpiry.StatusFilter(status, Now))
            .Select(e => e.Id)
            .ToHashSet();

        var shouldHaveBeenSelected = universe
            .Where(e => EnvelopeExpiry.Derive(e.Status, e.ExpiresAt, Now) == status)
            .Select(e => e.Id);

        foreach (var id in shouldHaveBeenSelected)
            Assert.Contains(id, selectedIds);
    }

    [Fact]
    public void An_overdue_request_stops_answering_to_the_status_it_is_stored_as()
    {
        // 这是"筛多了"最容易发生的形态：一份过了期的 Sent 同时出现在 Sent 和 Expired 两个筛选里。
        var overdue = At(EnvelopeStatus.Sent, Now.AddDays(-1));
        var universe = new[] { overdue }.AsQueryable();

        Assert.Empty(universe.Where(EnvelopeExpiry.StatusFilter(EnvelopeStatus.Sent, Now)));
        Assert.Single(universe.Where(EnvelopeExpiry.StatusFilter(EnvelopeStatus.Expired, Now)));
    }
}
