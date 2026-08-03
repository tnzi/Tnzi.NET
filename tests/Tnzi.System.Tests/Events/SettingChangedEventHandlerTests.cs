using Tnzi.System.Events;
using Tnzi.System.Events.Handlers;
using Tnzi.System.Hubs;
using Tnzi.SignalR.Services;

namespace Tnzi.System.Tests.Events;

/// <summary>
/// SettingChangedEventHandler 的实时广播分支测试：Global 作用域变更经
/// IMessagePushService&lt;SettingsRealtimeHub&gt; 广播 "Settings.Changed"；非 Global 不广播；
/// 无 SignalR（push=null）不抛。SettingConfigurationSource 的重载分支有独立测试，此处一律传 null。
/// </summary>
public class SettingChangedEventHandlerTests
{
    private static SettingChangedEventHandler Build(IMessagePushService<SettingsRealtimeHub>? push)
        => new(new Mock<ILogger<SettingChangedEventHandler>>().Object, source: null, push: push);

    [Fact]
    public async Task Global_Change_Broadcasts_SettingsChanged()
    {
        var push = new Mock<IMessagePushService<SettingsRealtimeHub>>();
        var handler = Build(push.Object);

        await handler.HandleAsync(new SettingChangedEvent
        {
            Key = "Chat:AllowInvisible",
            Scope = SettingScope.Global,
            IsRemoval = false,
        });

        push.Verify(p => p.PushToAllAsync("Settings.Changed", It.IsAny<object[]>()), Times.Once);
    }

    [Fact]
    public async Task NonGlobal_Change_DoesNotBroadcast()
    {
        var push = new Mock<IMessagePushService<SettingsRealtimeHub>>();
        var handler = Build(push.Object);

        await handler.HandleAsync(new SettingChangedEvent
        {
            Key = "Chat:AllowInvisible",
            Scope = SettingScope.User,
            ScopeId = "u1",
        });

        push.Verify(p => p.PushToAllAsync(It.IsAny<string>(), It.IsAny<object[]>()), Times.Never);
    }

    [Fact]
    public async Task NoSignalR_NullPush_DoesNotThrow()
    {
        var handler = Build(push: null);

        await Should.NotThrowAsync(async () => await handler.HandleAsync(new SettingChangedEvent
        {
            Key = "Appearance:AdminTheme",
            Scope = SettingScope.Global,
        }));
    }
}
