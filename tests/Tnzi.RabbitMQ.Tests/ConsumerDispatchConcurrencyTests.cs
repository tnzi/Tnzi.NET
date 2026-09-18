using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using Tnzi.EventBus;
using Tnzi.RabbitMQ.Options;

namespace Tnzi.RabbitMQ.Tests;

/// <summary>
/// 消费者 Channel 必须显式设置派发并发度。
/// </summary>
/// <remarks>
/// RabbitMQ.Client 7 的默认派发并发度是 1：同一 Channel 上的 <c>ReceivedAsync</c> 串行派发，
/// 派发器等上一条的回调返回才取下一条。而重试退避的 <c>Task.Delay</c>（1s / 2s / 4s）就跑在回调里 ——
/// 一条失败消息按指数退避阻塞该事件类型的<b>全部</b>消费，包括与它毫不相干的消息；
/// <c>PrefetchCount</c> 在这个前提下只是缓冲，处理仍是一条一条。
/// 派发器在客户端内部、Moq 碰不到，所以能断言的面是「交给客户端的 CreateChannelOptions」。
/// </remarks>
public class ConsumerDispatchConcurrencyTests
{
    private const string ConsumerGroup = "tests";

    private sealed class ProbeEvent : EventBase, IIntegrationEvent
    {
        public string SourceService => "tests";
    }

    private static (Mock<IConnection> Connection, List<CreateChannelOptions?> Captured) ConnectionCapturingChannelOptions()
    {
        var captured = new List<CreateChannelOptions?>();
        var channel = new Mock<IChannel>();
        channel.Setup(c => c.IsOpen).Returns(true);
        channel.Setup(c => c.QueueDeclareAsync(
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object?>>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueueDeclareOk("q", 0, 0));
        channel.Setup(c => c.BasicConsumeAsync(
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object?>>(), It.IsAny<IAsyncBasicConsumer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("consumer-tag");

        var connection = new Mock<IConnection>();
        connection
            .Setup(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
            .Callback<CreateChannelOptions?, CancellationToken>((options, _) => captured.Add(options))
            .ReturnsAsync(channel.Object);
        return (connection, captured);
    }

    private static RabbitMQEventBus CreateBus(Mock<IConnection> connection, RabbitMQOptions options)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return new RabbitMQEventBus(
            connection.Object,
            NullLogger<RabbitMQEventBus>.Instance,
            services.BuildServiceProvider(),
            options,
            consumerIdentity: new DistributedConsumerIdentity(ConsumerGroup, Guid.NewGuid()));
    }

    [Fact]
    public async Task ConsumerChannel_UsesTheConfiguredDispatchConcurrency()
    {
        var (connection, captured) = ConnectionCapturingChannelOptions();
        var bus = CreateBus(connection, new RabbitMQOptions { PrefetchCount = 10, ConsumerDispatchConcurrency = 4 });

        await bus.SubscribeEventAsync(typeof(ProbeEvent));

        var options = captured.ShouldHaveSingleItem();
        options.ShouldNotBeNull("没有 CreateChannelOptions 就是客户端默认值 1：一条失败消息的退避会阻塞整个事件类型");
        options.ConsumerDispatchConcurrency.ShouldBe((ushort)4);
    }

    [Fact]
    public async Task ConsumerChannel_DefaultsDispatchConcurrencyToPrefetchCount()
    {
        var (connection, captured) = ConnectionCapturingChannelOptions();
        var bus = CreateBus(connection, new RabbitMQOptions { PrefetchCount = 7 });

        await bus.SubscribeEventAsync(typeof(ProbeEvent));

        var options = captured.ShouldHaveSingleItem();
        options.ShouldNotBeNull();
        // 预取 N 条却只派发 1 条，其余 N-1 条只是躺在客户端缓冲里：并发度跟着预取走，PrefetchCount 的含义才成立
        options.ConsumerDispatchConcurrency.ShouldBe((ushort)7);
    }

    [Fact]
    public void Validator_RejectsZeroConsumerDispatchConcurrency()
    {
        var result = new RabbitMQOptionsValidator().Validate(null, new RabbitMQOptions { ConsumerDispatchConcurrency = 0 });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("ConsumerDispatchConcurrency");
    }

    [Fact]
    public void Validator_AcceptsUnsetConsumerDispatchConcurrency()
    {
        var result = new RabbitMQOptionsValidator().Validate(null, new RabbitMQOptions());

        result.Succeeded.ShouldBeTrue(result.FailureMessage);
    }
}
