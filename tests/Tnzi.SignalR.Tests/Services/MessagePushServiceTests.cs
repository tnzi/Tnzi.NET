using Microsoft.AspNetCore.SignalR;
using Tnzi.SignalR.Metadata;

namespace Tnzi.SignalR.Tests.Services;

/// <summary>
/// <see cref="MessagePushService{THub}"/> 的按用户寻址。
///
/// ★ 此前按用户推送先到进程内的 <see cref="IConnectionManager"/> 查连接 id，查不到就静默返回。
/// 多实例部署下连在别的实例上的用户在本地登记表里永远查不到，于是 Chat 的每一条点对点实时
/// 推送都有 (N-1)/N 的概率一条不发 —— REST 落库成功、界面显示已发送、日志零 Warning，
/// 而文档还写着「配了 Backplane 之后推送是跨实例正确的」。
///
/// 正确的原语是 SignalR 自己的组：<c>TnziHub.OnConnectedAsync</c> 已把每条已认证连接加进
/// <c>User_{userId}</c> 组，组投递由 backplane 转发，不依赖任何本地状态。
/// 这些测试刻意**不**提供任何连接登记 —— 推送必须在登记表为空时照样发出。
/// </summary>
public class MessagePushServiceTests
{
    public sealed class TestHub : Hub
    {
    }

    private sealed class Harness
    {
        public Mock<IHubClients> Clients { get; } = new(MockBehavior.Strict);
        public Mock<IClientProxy> Proxy { get; } = new();
        public List<string> SingleGroupTargets { get; } = [];
        public List<IReadOnlyList<string>> MultiGroupTargets { get; } = [];
        public List<(string Method, object?[] Args)> Sent { get; } = [];
        public MessagePushService<TestHub> Service { get; }

        public Harness()
        {
            Clients.Setup(c => c.Group(It.IsAny<string>()))
                .Callback<string>(SingleGroupTargets.Add)
                .Returns(Proxy.Object);
            Clients.Setup(c => c.Groups(It.IsAny<IReadOnlyList<string>>()))
                .Callback<IReadOnlyList<string>>(MultiGroupTargets.Add)
                .Returns(Proxy.Object);
            Proxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                .Callback<string, object?[], CancellationToken>((method, args, _) => Sent.Add((method, args)))
                .Returns(Task.CompletedTask);

            var hubContext = new Mock<IHubContext<TestHub>>();
            hubContext.SetupGet(h => h.Clients).Returns(Clients.Object);
            Service = new MessagePushService<TestHub>(hubContext.Object);
        }
    }

    [Fact]
    public async Task PushToUserAsync_TargetsTheUserGroup_WithoutAnyLocalConnectionRegistry()
    {
        var harness = new Harness();
        var userId = Guid.NewGuid();
        var payload = new { Text = "hi" };

        await harness.Service.PushToUserAsync(userId, "Chat.NewMessage", payload);

        harness.SingleGroupTargets.ShouldBe([HubGroupNames.ForUser(userId)]);
        harness.Sent.Count.ShouldBe(1);
        harness.Sent[0].Method.ShouldBe("Chat.NewMessage");
        harness.Sent[0].Args.ShouldBe([payload]);
    }

    [Fact]
    public async Task PushToUsersAsync_TargetsEveryUserGroup_InOneSend()
    {
        var harness = new Harness();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await harness.Service.PushToUsersAsync([a, b], "Chat.MessageRead", 42);

        harness.MultiGroupTargets.Count.ShouldBe(1);
        harness.MultiGroupTargets[0].ShouldBe([HubGroupNames.ForUser(a), HubGroupNames.ForUser(b)]);
        harness.Sent.Count.ShouldBe(1);
        harness.Sent[0].Method.ShouldBe("Chat.MessageRead");
        harness.Sent[0].Args.ShouldBe([42]);
    }

    [Fact]
    public async Task PushToUsersAsync_DeduplicatesRepeatedUserIds()
    {
        var harness = new Harness();
        var a = Guid.NewGuid();

        await harness.Service.PushToUsersAsync([a, a], "Chat.ConversationChanged");

        harness.MultiGroupTargets.Count.ShouldBe(1);
        harness.MultiGroupTargets[0].ShouldBe([HubGroupNames.ForUser(a)]);
        harness.Sent.Count.ShouldBe(1);
    }

    [Fact]
    public async Task PushToUsersAsync_WithNoRecipients_SendsNothing()
    {
        var harness = new Harness();

        await harness.Service.PushToUsersAsync([], "Chat.NewMessage");

        harness.MultiGroupTargets.ShouldBeEmpty();
        harness.Sent.ShouldBeEmpty();
    }

    /// <summary>
    /// 组名必须与 <c>TnziHub.OnConnectedAsync</c> 加入的逐字一致；两边共用一个来源，
    /// 这条测试守的是「那个来源没有被绕开」。
    /// </summary>
    [Fact]
    public void HubGroupNames_ForUser_MatchesTheConventionTnziHubJoins()
    {
        var userId = Guid.NewGuid();

        HubGroupNames.ForUser(userId).ShouldBe($"User_{userId}");
    }
}
