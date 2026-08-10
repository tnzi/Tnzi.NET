namespace Tnzi.Identity.Presence.Tests;

/// <summary>
/// 隐身的边界：连接生灭在 presence 行上留下什么痕迹、什么时候该广播。
/// </summary>
/// <remarks>
/// <para>
/// <b>被保护的缺陷</b>：<c>PresenceResolver</c> 一直老老实实把 Invisible 解析成 Offline，
/// 但 <c>MarkOfflineAsync</c> 在隐身用户真正断开时照样写 <c>LastSeenAt = now</c>，
/// 并照样广播一条 <c>Presence.Changed</c>。于是任何旁观者都能看到：一个"一直离线"的人，
/// 他的"最后在线时间"突然跳到此刻 —— <b>隐身的人其实一直在，而且刚刚才真的离开</b>。
/// 状态字段守住了，时间戳和广播时机没守住。
/// </para>
/// <para>
/// ★ 更糟的是 <c>PresenceRealtimePushHandler</c> 是<b>全量广播</b>：这条推送发给所有在线客户端，
/// 不需要抓包，开着开发者工具就看得见。
/// </para>
/// <para>
/// <b>正确的语义</b>：<c>LastSeenAt</c> = 最后一次<b>被看得见</b>的时刻，不是最后一次断开的时刻。
/// 对旁观者而言隐身就是下线，所以隐身那一刻盖章，此后真的断开时什么都不发生 ——
/// 这样隐身用户与"此刻关掉了应用的人"在数据上逐字一致，没有任何可区分之处。
/// </para>
/// </remarks>
public class PresenceDisclosureTests
{
    private static readonly DateTime WentInvisibleAt = new(2026, 8, 8, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ActuallyLeftAt = new(2026, 8, 8, 17, 30, 0, DateTimeKind.Utc);

    private static UserPresence Row(UserPresenceStatus status, DateTime? lastSeenAt = null)
        => new()
        {
            UserId = Guid.NewGuid(),
            Status = status,
            LastSeenAt = lastSeenAt,
            LastChangedAt = WentInvisibleAt
        };

    /// <summary>
    /// 隐身者真正断开时，行上一个字都不能动，也不该广播。
    /// </summary>
    [Fact]
    public void Disconnecting_WhileInvisible_LeavesNoTrace()
    {
        var row = Row(UserPresenceStatus.Invisible, WentInvisibleAt);

        var changed = PresenceDisclosure.ApplyDisconnected(row, ActuallyLeftAt, allowInvisible: true);

        changed.ShouldBeFalse("广播一条状态变化等于宣告这个显示为离线的人刚刚才真的走");
        row.LastSeenAt.ShouldBe(WentInvisibleAt, "最后可见时刻在隐身那一刻就盖过章了，不该跳到此刻");
        row.LastChangedAt.ShouldBe(WentInvisibleAt);
    }

    /// <summary>
    /// 普通用户断开时照常记时间并广播 —— 防止把守卫做成"谁都不记"。
    /// </summary>
    [Theory]
    [InlineData(UserPresenceStatus.Online)]
    [InlineData(UserPresenceStatus.Away)]
    [InlineData(UserPresenceStatus.Busy)]
    public void Disconnecting_WhileVisible_RecordsAndBroadcasts(UserPresenceStatus intent)
    {
        var row = Row(intent, lastSeenAt: null);

        var changed = PresenceDisclosure.ApplyDisconnected(row, ActuallyLeftAt, allowInvisible: true);

        changed.ShouldBeTrue();
        row.LastSeenAt.ShouldBe(ActuallyLeftAt);
        row.LastChangedAt.ShouldBe(ActuallyLeftAt);
    }

    /// <summary>
    /// 部署关掉隐身时，历史隐身意图不再享有豁免。
    /// </summary>
    /// <remarks>
    /// 判据必须与 <see cref="PresenceResolver.Resolve"/> 同源：那边 <c>AllowInvisible=false</c> 时
    /// 把 Invisible 按 Online 解析（此人对外可见），这边就不能还当他是隐身的 ——
    /// 否则会出现"显示为在线、却永远停在某个古老的最后在线时间"这种自相矛盾的数据。
    /// </remarks>
    [Fact]
    public void Disconnecting_WhileInvisible_ButInvisibilityIsDisabled_RecordsNormally()
    {
        var row = Row(UserPresenceStatus.Invisible, WentInvisibleAt);

        var changed = PresenceDisclosure.ApplyDisconnected(row, ActuallyLeftAt, allowInvisible: false);

        changed.ShouldBeTrue();
        row.LastSeenAt.ShouldBe(ActuallyLeftAt);
    }

    /// <summary>
    /// 隐身者连上来同样无声 —— 上线方向的对称泄露。
    /// </summary>
    /// <remarks>
    /// 只堵下线不堵上线是没用的：全量广播里冒出一条关于某人的推送，本身就说明他刚做了什么。
    /// </remarks>
    [Fact]
    public void Connecting_WhileInvisible_DoesNotBroadcast()
    {
        var row = Row(UserPresenceStatus.Invisible, WentInvisibleAt);

        var changed = PresenceDisclosure.ApplyConnected(row, ActuallyLeftAt, allowInvisible: true);

        changed.ShouldBeFalse();
        row.LastChangedAt.ShouldBe(WentInvisibleAt);
        row.LastSeenAt.ShouldBe(WentInvisibleAt);
    }

    /// <summary>
    /// 但活动时间照记：它是 auto-away 的内部记账，不对外披露。
    /// </summary>
    /// <remarks>
    /// 连 <c>LastActivityAt</c> 一起冻住会让隐身用户从隐身状态切回在线时被误判成空闲。
    /// 判断"什么该冻结"的依据是**这个字段有没有出口**，不是"它听起来敏不敏感"。
    /// </remarks>
    [Fact]
    public void Connecting_WhileInvisible_StillTracksActivityForAutoAway()
    {
        var row = Row(UserPresenceStatus.Invisible, WentInvisibleAt);
        row.IsAutoAway = true;

        PresenceDisclosure.ApplyConnected(row, ActuallyLeftAt, allowInvisible: true);

        row.LastActivityAt.ShouldBe(ActuallyLeftAt);
        row.IsAutoAway.ShouldBeFalse();
    }

    /// <summary>
    /// 从看得见切到隐身：最后可见时刻定在此刻。
    /// </summary>
    [Fact]
    public void GoingInvisible_StampsTheMomentTheyStoppedBeingVisible()
    {
        var row = Row(UserPresenceStatus.Online, lastSeenAt: null);

        PresenceDisclosure.ApplyIntent(row, UserPresenceStatus.Invisible, WentInvisibleAt, goingDark: true);

        row.Status.ShouldBe(UserPresenceStatus.Invisible);
        row.LastSeenAt.ShouldBe(WentInvisibleAt,
            "旁观者此刻看到他消失，就该看到「最后在线：此刻」；不盖章会显示成一个久远的时间，" +
            "反而暴露了「这个人是隐身而不是下线」");
    }

    /// <summary>
    /// 隐身期间再设一次隐身不重新盖章 —— 否则时间戳会在隐身期间一路往前走。
    /// </summary>
    [Fact]
    public void SettingInvisibleAgain_WhileAlreadyInvisible_DoesNotRestampLastSeen()
    {
        var row = Row(UserPresenceStatus.Invisible, WentInvisibleAt);

        PresenceDisclosure.ApplyIntent(row, UserPresenceStatus.Invisible, ActuallyLeftAt, goingDark: false);

        row.LastSeenAt.ShouldBe(WentInvisibleAt);
    }

    /// <summary>
    /// 该不该盖章，由<b>切换前旁观者看到的状态</b>决定，不是由新意图决定。
    /// </summary>
    /// <remarks>
    /// 三条里只有第一条该盖章。剩下两条正是"只看新意图"会犯的两个方向相反的错：
    /// 隐身期间反复盖章让时间戳一路往前走；给本来就离线的人盖章又把它往后推成"刚刚还在线"。
    /// </remarks>
    [Theory]
    // 在线 → 隐身：旁观者此刻看到他消失，盖章
    [InlineData(UserPresenceStatus.Invisible, UserPresenceStatus.Online, true)]
    [InlineData(UserPresenceStatus.Invisible, UserPresenceStatus.Away, true)]
    [InlineData(UserPresenceStatus.Invisible, UserPresenceStatus.Busy, true)]
    // 已经隐身（有效状态即 Offline）再点一次隐身：旁观者什么也没看到，不盖章
    [InlineData(UserPresenceStatus.Invisible, UserPresenceStatus.Offline, false)]
    // 切到可见状态：这个字段轮不到它说话
    [InlineData(UserPresenceStatus.Online, UserPresenceStatus.Offline, false)]
    [InlineData(UserPresenceStatus.Busy, UserPresenceStatus.Online, false)]
    public void GoingDark_IsDecidedByWhatOnlookersCouldSeeBefore(
        UserPresenceStatus newIntent, UserPresenceStatus effectiveBefore, bool expected)
    {
        PresenceDisclosure.IsGoingDark(newIntent, effectiveBefore).ShouldBe(expected);
    }

    /// <summary>
    /// 切回可见不动最后可见时刻 —— 他现在就在线上，这个字段轮不到它说话。
    /// </summary>
    [Fact]
    public void ComingBackVisible_DoesNotTouchLastSeen()
    {
        var row = Row(UserPresenceStatus.Invisible, WentInvisibleAt);

        PresenceDisclosure.ApplyIntent(row, UserPresenceStatus.Online, ActuallyLeftAt, goingDark: false);

        row.Status.ShouldBe(UserPresenceStatus.Online);
        row.LastSeenAt.ShouldBe(WentInvisibleAt);
    }

    /// <summary>
    /// 完整一遍：隐身用户从上线到真正离开，旁观者看到的数据与「一直离线」的人完全一致。
    /// </summary>
    /// <remarks>
    /// 逐步断言不如这一条说得清楚：隐身要成立，靠的不是某一处判断对了，
    /// 而是<b>整条时间线上没有任何一个可观测量发生过变化</b>。
    /// </remarks>
    [Fact]
    public void AcrossAFullInvisibleSession_NothingObservableEverChanges()
    {
        var lastVisibleSession = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        var row = Row(UserPresenceStatus.Online, lastVisibleSession);

        // 09:00 切到隐身（此时在线，故盖章）
        PresenceDisclosure.ApplyIntent(row, UserPresenceStatus.Invisible, WentInvisibleAt, goingDark: true);
        var stamped = row.LastSeenAt;
        var changedAt = row.LastChangedAt;

        // 隐身期间断线重连若干次
        PresenceDisclosure.ApplyDisconnected(row, WentInvisibleAt.AddHours(1), allowInvisible: true).ShouldBeFalse();
        PresenceDisclosure.ApplyConnected(row, WentInvisibleAt.AddHours(2), allowInvisible: true).ShouldBeFalse();
        PresenceDisclosure.ApplyDisconnected(row, WentInvisibleAt.AddHours(3), allowInvisible: true).ShouldBeFalse();

        // 17:30 真的关掉应用
        PresenceDisclosure.ApplyDisconnected(row, ActuallyLeftAt, allowInvisible: true).ShouldBeFalse();

        row.LastSeenAt.ShouldBe(stamped, "隐身期间任何一次断连都不得移动最后可见时刻");
        row.LastChangedAt.ShouldBe(changedAt);
    }
}
