using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Tnzi.EventBus;
using Tnzi.RabbitMQ.Exceptions;
using Tnzi.RabbitMQ.Options;

namespace Tnzi.RabbitMQ.Tests;

/// <summary>
/// 发布必须等代理确认：<c>PublishAsync</c> 返回成功，意思是代理已经接下这条消息，而不是帧已经写出去。
/// </summary>
/// <remarks>
/// <para>
/// 没有发布确认时 <c>BasicPublishAsync</c> 写完帧即返回。Outbox 中继据此把行标成已处理；
/// 帧写出后连接断开、或代理在落盘前崩溃，消息就没了而 Outbox 说它投递过了，
/// 至少一次的承诺在这个传输上有缺口。消费侧重试副本同理：副本没被代理接下就确认了原消息。
/// </para>
/// <para>
/// 确认的等待在客户端内部、Moq 碰不到，所以能断言的面是「交给客户端的 CreateChannelOptions」
/// 与「客户端抛出的 PublishException 怎样被处置」。
/// </para>
/// </remarks>
public class PublisherConfirmTests
{
    private sealed class ProbeEvent : EventBase, IIntegrationEvent
    {
        public string SourceService => "tests";
    }

    private sealed class Broker
    {
        public List<CreateChannelOptions?> ChannelOptions { get; } = [];
        public Mock<IConnection> Connection { get; } = new();
        public Mock<IChannel> Channel { get; } = new();

        public Broker()
        {
            Channel.Setup(c => c.IsOpen).Returns(true);
            Channel.Setup(c => c.BasicPublishAsync<BasicProperties>(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                    It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask());

            Connection
                .Setup(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
                .Callback<CreateChannelOptions?, CancellationToken>((options, _) => ChannelOptions.Add(options))
                .ReturnsAsync(Channel.Object);
        }

        public void PublishThrows(Exception exception)
            => Channel.Setup(c => c.BasicPublishAsync<BasicProperties>(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                    It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.FromException(exception));

        public RabbitMQEventBus CreateBus(RabbitMQOptions? options = null)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            return new RabbitMQEventBus(
                Connection.Object,
                NullLogger<RabbitMQEventBus>.Instance,
                services.BuildServiceProvider(),
                options ?? new RabbitMQOptions(),
                consumerIdentity: new DistributedConsumerIdentity("tests", Guid.NewGuid()));
        }
    }

    [Fact]
    public async Task ThePublishChannel_WaitsForBrokerConfirmations()
    {
        var broker = new Broker();
        await using var bus = broker.CreateBus();

        await bus.PublishAsync(new ProbeEvent());

        var options = broker.ChannelOptions.ShouldHaveSingleItem().ShouldNotBeNull(
            "不传 CreateChannelOptions 就是客户端默认值：不开发布确认，写完帧即算发布成功");
        options.PublisherConfirmationsEnabled.ShouldBeTrue();
        options.PublisherConfirmationTrackingEnabled.ShouldBeTrue(
            "不开跟踪的话 BasicPublishAsync 不等确认、代理的 nack 也到不了发布方");
    }

    [Fact]
    public async Task PooledPublishChannels_WaitForBrokerConfirmations()
    {
        var broker = new Broker();
        await using var bus = broker.CreateBus(new RabbitMQOptions
        {
            ChannelPool = new ChannelPoolOptions { Enabled = true, MaxSize = 2 }
        });

        await bus.PublishAsync(new ProbeEvent());

        var options = broker.ChannelOptions.ShouldHaveSingleItem().ShouldNotBeNull();
        options.PublisherConfirmationsEnabled.ShouldBeTrue();
        options.PublisherConfirmationTrackingEnabled.ShouldBeTrue();
    }

    /// <summary>代理 nack：发布失败，调用方（Outbox 中继）据此计一次失败、稍后重发，而不是把行标成已处理。</summary>
    [Fact]
    public async Task ABrokerNack_FailsThePublish()
    {
        var broker = new Broker();
        broker.PublishThrows(new PublishException(1, isReturn: false));
        await using var bus = broker.CreateBus();

        var thrown = await Should.ThrowAsync<RabbitMQException>(() => bus.PublishAsync(new ProbeEvent()));

        thrown.InnerException.ShouldBeOfType<PublishException>();
    }

    /// <summary>
    /// 退回（没有任何队列绑定这个事件）不是发布失败：代理接下并确认了消息，只是没有消费者。
    /// 退回由 Channel 上的 basic.return 处理记一条 Error，发布照常完成。
    /// </summary>
    [Fact]
    public async Task AnUnroutableReturn_DoesNotFailThePublish()
    {
        var broker = new Broker();
        broker.PublishThrows(new PublishReturnException(1, "unroutable", "Tnzi.Events", "probe", 312, "NO_ROUTE"));
        await using var bus = broker.CreateBus();

        await Should.NotThrowAsync(() => bus.PublishAsync(new ProbeEvent()));
    }
}
