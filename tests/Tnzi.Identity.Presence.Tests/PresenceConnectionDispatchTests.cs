using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Tnzi.EventBus;
using Tnzi.Identity.Presence.Events.Handlers;
using Tnzi.SignalR.Events;

namespace Tnzi.Identity.Presence.Tests;

/// <summary>
/// <see cref="PresenceConnectionEventHandler"/> 的<b>派发</b>层保障：两个连接事件都要真的落到各自的方法。
/// </summary>
/// <remarks>
/// <para>
/// 这个处理器一个类实现两个 <c>IEventHandler&lt;&gt;</c>。2026-09-09 以前，事件调用器按处理器类型单键
/// 缓存编译委托并只绑定反射返回的第一个事件接口，于是<b>只有一个方向真的会跑</b>：
/// 生产环境每次用户断线都抛 <c>InvalidCastException</c>，
/// <see cref="IPresenceService.MarkOfflineAsync"/> 从未被调用 —— <c>LastSeenAt</c> 不写、没人被置为离线，
/// 而每条断线事件还白烧一轮重试后进死信队列。
/// </para>
/// <para>
/// 用例刻意<b>走真实的 <see cref="LocalEventBus"/></b>：直接调 <c>HandleAsync</c> 的单元测试
/// 对这个缺陷完全免疫（方法本身没毛病，坏的是派发选错了接口），
/// 而这个处理器早就有直接调用式的覆盖，缺陷照样在生产上活了下来。
/// </para>
/// <para>
/// 处理器标了 <c>[BackgroundEventHandler]</c>（fire-and-forget），
/// 因此断言前用 <c>DisposeAsync</c> 等在飞的后台任务排水，避免竞态。
/// </para>
/// </remarks>
public class PresenceConnectionDispatchTests
{
    private static (LocalEventBus Bus, Mock<IPresenceService> Presence) CreateBus()
    {
        var presence = new Mock<IPresenceService>();
        presence.Setup(p => p.MarkActiveAsync(It.IsAny<Guid>())).ReturnsAsync(true);
        presence.Setup(p => p.MarkOfflineAsync(It.IsAny<Guid>())).ReturnsAsync(true);
        presence.Setup(p => p.NotifyChangedAsync(It.IsAny<Guid>())).Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(presence.Object);

        // 与 IdentityPresenceModule.ConfigureServicesAsync 中逐字相同的两行注册
        services.AddEventHandler<UserConnectedEvent, PresenceConnectionEventHandler>();
        services.AddEventHandler<UserDisconnectedEvent, PresenceConnectionEventHandler>();

        var provider = services.BuildServiceProvider();
        var bus = new LocalEventBus(provider, provider.GetRequiredService<ILogger<LocalEventBus>>());
        return (bus, presence);
    }

    [Fact]
    public async Task Publish_UserDisconnected_ReachesMarkOffline()
    {
        var userId = Guid.NewGuid();
        var (bus, presence) = CreateBus();

        await bus.PublishAsync(new UserDisconnectedEvent
        {
            UserId = userId,
            ConnectionId = "c1",
            HubName = "TestHub",
            WentOffline = true
        });

        await bus.DisposeAsync(); // 排水：后台处理器是 fire-and-forget

        presence.Verify(p => p.MarkOfflineAsync(userId), Times.Once);
    }

    [Fact]
    public async Task Publish_UserConnected_ReachesMarkActive()
    {
        var userId = Guid.NewGuid();
        var (bus, presence) = CreateBus();

        await bus.PublishAsync(new UserConnectedEvent
        {
            UserId = userId,
            ConnectionId = "c1",
            HubName = "TestHub",
            TotalConnections = 1
        });

        await bus.DisposeAsync();

        presence.Verify(p => p.MarkActiveAsync(userId), Times.Once);
    }

    /// <summary>
    /// 两个方向在同一条总线上先后派发：缺陷期两者必有一个静默失效。
    /// </summary>
    [Fact]
    public async Task Publish_BothConnectionEvents_ReachBothBranches()
    {
        var userId = Guid.NewGuid();
        var (bus, presence) = CreateBus();

        await bus.PublishAsync(new UserConnectedEvent
        {
            UserId = userId,
            ConnectionId = "c1",
            HubName = "TestHub",
            TotalConnections = 1
        });
        await bus.PublishAsync(new UserDisconnectedEvent
        {
            UserId = userId,
            ConnectionId = "c1",
            HubName = "TestHub",
            WentOffline = true
        });

        await bus.DisposeAsync();

        presence.Verify(p => p.MarkActiveAsync(userId), Times.Once);
        presence.Verify(p => p.MarkOfflineAsync(userId), Times.Once);
    }
}
