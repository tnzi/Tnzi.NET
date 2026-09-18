using Microsoft.Extensions.Logging;
using Moq;
using Tnzi.Identity.Presence.Events;
using Tnzi.Identity.Presence.Events.Handlers;
using Tnzi.Identity.Presence.Hubs;
using Tnzi.SignalR.Metadata;

namespace Tnzi.Identity.Presence.Tests;

/// <summary>
/// <see cref="PresenceRealtimePushHandler"/> 的投递范围：带租户的事件只发给该租户的连接。
/// </summary>
/// <remarks>
/// ★ 此前一律 <c>PushToAllAsync</c>。「开放目录下全量广播不构成隐私回退」的前提是单租户目录；
/// 多租户开启时 <c>Clients.All</c> 把租户 B 每一次上下线（userId / status / lastSeenAt）送到租户 A 的
/// 每一个连接，与 Chat 广播刻意拒绝跨租户 All 的标准相反。事件自带 <c>TenantId</c>（总线从
/// <c>ICurrentTenant</c> 捕获），处理器手里一直有它却没用。
/// </remarks>
public class PresenceRealtimePushHandlerTests
{
    private readonly Mock<IMessagePushService<PresenceHub>> _push = new();
    private readonly PresenceRealtimePushHandler _handler;

    public PresenceRealtimePushHandlerTests()
    {
        _handler = new PresenceRealtimePushHandler(new Mock<ILogger<PresenceRealtimePushHandler>>().Object, _push.Object);
    }

    [Fact]
    public async Task WithATenant_PushesToTheTenantGroup_NeverToAll()
    {
        var tenantId = Guid.NewGuid();
        var @event = new UserPresenceChangedEvent { UserId = Guid.NewGuid(), Status = UserPresenceStatus.Online, TenantId = tenantId };

        await _handler.HandleAsync(@event);

        _push.Verify(
            p => p.PushToGroupAsync(HubGroupNames.ForTenant(tenantId), PresenceRealtimePushHandler.PresenceChangedMethod, It.IsAny<object[]>()),
            Times.Once);
        _push.Verify(p => p.PushToAllAsync(It.IsAny<string>(), It.IsAny<object[]>()), Times.Never);
    }

    /// <summary>多租户未开启时事件没有租户，行为逐字不变：全量广播。</summary>
    [Fact]
    public async Task WithoutATenant_StillPushesToAll()
    {
        var @event = new UserPresenceChangedEvent { UserId = Guid.NewGuid(), Status = UserPresenceStatus.Offline, TenantId = null };

        await _handler.HandleAsync(@event);

        _push.Verify(p => p.PushToAllAsync(PresenceRealtimePushHandler.PresenceChangedMethod, It.IsAny<object[]>()), Times.Once);
        _push.Verify(p => p.PushToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object[]>()), Times.Never);
    }
}
