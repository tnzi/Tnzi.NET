using Tnzi.System.Events;
using Tnzi.System.Events.Handlers;
using Tnzi.System.Hubs;
using Tnzi.SignalR.Services;

namespace Tnzi.System.Tests.Events;

/// <summary>
/// 分布式收端（多实例一致性）测试：其他实例的 Global 变更 → 清本实例按键缓存 +
/// 复用本地 handler（reload/广播）；自己实例的回环投递被跳过（本地链已处理）。
/// </summary>
public class SettingChangedDistributedEventHandlerTests
{
    private static SettingChangedDistributedEventHandler Build(
        Mock<ICache> cache, IMessagePushService<SettingsRealtimeHub>? push = null)
    {
        var local = new SettingChangedEventHandler(
            new Mock<ILogger<SettingChangedEventHandler>>().Object, source: null, push: push);
        return new SettingChangedDistributedEventHandler(
            local, cache.Object, new Mock<ILogger<SettingChangedDistributedEventHandler>>().Object);
    }

    [Fact]
    public async Task Foreign_Instance_Change_Clears_Cache_And_Rebroadcasts_Locally()
    {
        var cache = new Mock<ICache>();
        var push = new Mock<IMessagePushService<SettingsRealtimeHub>>();
        var handler = Build(cache, push.Object);

        await handler.HandleAsync(new SettingChangedIntegrationEvent
        {
            Key = "Chat:AllowInvisible",
            Scope = SettingScope.Global,
            OriginInstanceId = Guid.NewGuid(), // 非本实例
        });

        cache.Verify(c => c.RemoveAsync("Setting:Chat:AllowInvisible"), Times.Once);
        push.Verify(p => p.PushToAllAsync("Settings.Changed", It.IsAny<object[]>()), Times.Once);
    }

    [Fact]
    public async Task Own_Instance_Loopback_Is_Skipped()
    {
        var cache = new Mock<ICache>();
        var push = new Mock<IMessagePushService<SettingsRealtimeHub>>();
        var handler = Build(cache, push.Object);

        await handler.HandleAsync(new SettingChangedIntegrationEvent
        {
            Key = "Chat:AllowInvisible",
            Scope = SettingScope.Global,
            OriginInstanceId = SettingChangedIntegrationEvent.LocalInstanceId, // 本实例发布的回环
        });

        cache.Verify(c => c.RemoveAsync(It.IsAny<string>()), Times.Never);
        push.Verify(p => p.PushToAllAsync(It.IsAny<string>(), It.IsAny<object[]>()), Times.Never);
    }
}
