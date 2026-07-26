namespace Tnzi.Identity.Presence.Tests;

/// <summary>有效状态解析纯函数的解析矩阵（含 auto-away）。</summary>
public class PresenceResolverTests
{
    [Fact]
    public void Online_Connected_Tracking_Stays_Online()
        => PresenceResolver.Resolve(UserPresenceStatus.Online, hasConnection: true, connectionTracking: true,
            allowInvisible: true, isAutoAway: false, autoAwayEnabled: true)
            .ShouldBe(UserPresenceStatus.Online);

    [Fact]
    public void Online_NotConnected_Tracking_Is_Offline()
        => PresenceResolver.Resolve(UserPresenceStatus.Online, hasConnection: false, connectionTracking: true,
            allowInvisible: true, isAutoAway: false, autoAwayEnabled: true)
            .ShouldBe(UserPresenceStatus.Offline);

    [Fact]
    public void Invisible_Connected_Is_Offline()
        => PresenceResolver.Resolve(UserPresenceStatus.Invisible, hasConnection: true, connectionTracking: true,
            allowInvisible: true, isAutoAway: false, autoAwayEnabled: true)
            .ShouldBe(UserPresenceStatus.Offline);

    [Fact]
    public void Invisible_When_Disabled_Resolves_Online_If_Connected()
        => PresenceResolver.Resolve(UserPresenceStatus.Invisible, hasConnection: true, connectionTracking: true,
            allowInvisible: false, isAutoAway: false, autoAwayEnabled: true)
            .ShouldBe(UserPresenceStatus.Online);

    [Fact]
    public void Busy_Connected_Stays_Busy()
        => PresenceResolver.Resolve(UserPresenceStatus.Busy, hasConnection: true, connectionTracking: true,
            allowInvisible: true, isAutoAway: false, autoAwayEnabled: true)
            .ShouldBe(UserPresenceStatus.Busy);

    [Fact]
    public void ManualOnly_NoTracking_Shows_Intent()
        => PresenceResolver.Resolve(UserPresenceStatus.Away, hasConnection: false, connectionTracking: false,
            allowInvisible: true, isAutoAway: false, autoAwayEnabled: true)
            .ShouldBe(UserPresenceStatus.Away);

    // ── auto-away ─────────────────────────────────────────────────────────────

    [Fact]
    public void Online_Idle_Connected_Resolves_Away_When_AutoAway_On()
        => PresenceResolver.Resolve(UserPresenceStatus.Online, hasConnection: true, connectionTracking: true,
            allowInvisible: true, isAutoAway: true, autoAwayEnabled: true)
            .ShouldBe(UserPresenceStatus.Away);

    [Fact]
    public void Online_Idle_Stays_Online_When_AutoAway_Off()
        => PresenceResolver.Resolve(UserPresenceStatus.Online, hasConnection: true, connectionTracking: true,
            allowInvisible: true, isAutoAway: true, autoAwayEnabled: false)
            .ShouldBe(UserPresenceStatus.Online);

    [Fact]
    public void Busy_Idle_Stays_Busy_AutoAway_Only_Applies_To_Online()
        => PresenceResolver.Resolve(UserPresenceStatus.Busy, hasConnection: true, connectionTracking: true,
            allowInvisible: true, isAutoAway: true, autoAwayEnabled: true)
            .ShouldBe(UserPresenceStatus.Busy);

    [Fact]
    public void Online_Idle_NotConnected_Still_Offline()
        => PresenceResolver.Resolve(UserPresenceStatus.Online, hasConnection: false, connectionTracking: true,
            allowInvisible: true, isAutoAway: true, autoAwayEnabled: true)
            .ShouldBe(UserPresenceStatus.Offline);
}
