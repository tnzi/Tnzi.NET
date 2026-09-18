namespace Tnzi.Tests.EventBus;

/// <summary>
/// 分布式订阅的身份与命名：队列 / 消费者组必须带上「哪个服务」「哪个实例」。
/// </summary>
/// <remarks>
/// <b>被保护的缺陷</b>：队列名此前只由事件类型决定（<c>Tnzi.Events.{事件全名}</c>），
/// 同一应用的 N 个实例和共享代理的任意两个服务都在竞争消费同一条队列 ——
/// 每条消息只到达其中一个进程，没有任何症状。
/// </remarks>
public class DistributedConsumerIdentityTests
{
    public class WorkEvent : EventBase, IIntegrationEvent
    {
        public string SourceService => "tests";
    }

    public class FanOutEvent : EventBase, IBroadcastIntegrationEvent
    {
        public string SourceService => "tests";
    }

    [Fact]
    public void SharedSubscription_IsNamedByConsumerGroupAndEvent()
    {
        var identity = new DistributedConsumerIdentity("orders");

        var subscription = identity.Describe(typeof(WorkEvent));

        Assert.False(subscription.IsBroadcast);
        Assert.Equal($"orders.{typeof(WorkEvent).FullName}", subscription.ConsumerName);
        Assert.Equal($"orders.DeadLetter.{typeof(WorkEvent).FullName}", subscription.DeadLetterName);
        Assert.Equal(typeof(WorkEvent).FullName, subscription.EventName);
    }

    [Fact]
    public void TwoConsumerGroups_GetDistinctNamesForTheSameEvent()
    {
        var orders = new DistributedConsumerIdentity("orders").Describe(typeof(WorkEvent));
        var notifications = new DistributedConsumerIdentity("notifications").Describe(typeof(WorkEvent));

        Assert.NotEqual(orders.ConsumerName, notifications.ConsumerName);
        Assert.Equal(orders.EventName, notifications.EventName);
    }

    [Fact]
    public void BroadcastSubscription_IsNamedPerInstance()
    {
        var instanceA = Guid.NewGuid();
        var instanceB = Guid.NewGuid();

        var a = new DistributedConsumerIdentity("orders", instanceA).Describe(typeof(FanOutEvent));
        var b = new DistributedConsumerIdentity("orders", instanceB).Describe(typeof(FanOutEvent));

        Assert.True(a.IsBroadcast);
        Assert.Equal($"orders.{typeof(FanOutEvent).FullName}.{instanceA:N}", a.ConsumerName);
        Assert.NotEqual(a.ConsumerName, b.ConsumerName);
    }

    [Fact]
    public void InstanceId_DefaultsToTheProcessInstance()
    {
        var identity = new DistributedConsumerIdentity("orders");

        Assert.Equal(TnziInstance.Id, identity.InstanceId);
    }

    [Fact]
    public void IsBroadcast_FollowsTheMarkerInterface()
    {
        Assert.True(DistributedConsumerIdentity.IsBroadcast(typeof(FanOutEvent)));
        Assert.False(DistributedConsumerIdentity.IsBroadcast(typeof(WorkEvent)));
    }

    [Fact]
    public void ResolveConsumerGroup_PrefersTheConfiguredValue()
    {
        Assert.Equal("orders", DistributedConsumerIdentity.ResolveConsumerGroup("  orders "));
    }

    [Fact]
    public void ResolveConsumerGroup_FallsBackToTheEntryAssemblyName()
    {
        var expected = Assembly.GetEntryAssembly()?.GetName().Name ?? DistributedConsumerIdentity.FallbackConsumerGroup;

        Assert.Equal(expected, DistributedConsumerIdentity.ResolveConsumerGroup(null));
        Assert.Equal(expected, DistributedConsumerIdentity.ResolveConsumerGroup("   "));
    }

    [Fact]
    public void FromOptions_UsesTheConfiguredConsumerGroup()
    {
        var identity = DistributedConsumerIdentity.FromOptions(new EventBusOptions { ConsumerGroup = "billing" });

        Assert.Equal("billing", identity.ConsumerGroup);
    }

    [Fact]
    public void ABlankConsumerGroup_IsRejected()
    {
        Assert.ThrowsAny<ArgumentException>(() => new DistributedConsumerIdentity("  "));
    }

    [Fact]
    public void Validator_RejectsABlankConfiguredConsumerGroup()
    {
        var validator = new EventBusOptionsValidator();

        var result = validator.Validate(null, new EventBusOptions { ConsumerGroup = "   " });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("ConsumerGroup"));
    }

    [Fact]
    public void Validator_AcceptsAnOmittedConsumerGroup()
    {
        var validator = new EventBusOptionsValidator();

        var result = validator.Validate(null, new EventBusOptions());

        Assert.True(result.Succeeded);
    }
}
