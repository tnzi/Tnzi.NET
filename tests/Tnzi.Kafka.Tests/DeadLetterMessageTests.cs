using System.Text;
using Confluent.Kafka;

namespace Tnzi.Kafka.Tests;

/// <summary>
/// 死信消息必须带上「是哪个消费者组把它判死的」。
/// </summary>
/// <remarks>
/// 死信主题 <c>{topic}.dlq</c> 刻意跨组共享（只记录不重消费）：同一主题的每个组、广播的每个实例都往同一处写。
/// 没有组标记，运维看着 <c>.dlq</c> 分不出是哪个服务失败的，而按主题整体重放会把消息重投给<b>每一个</b>组 ——
/// 包括那些本来就处理成功的。<c>x-dead-letter-consumer-group</c> 是重放工具按组过滤的唯一依据。
/// </remarks>
public class DeadLetterMessageTests
{
    private static Message<string, string> Original() => new()
    {
        Key = "k1",
        Value = "{\"id\":1}",
        Headers = new Headers { { "x-original", Encoding.UTF8.GetBytes("keep") } }
    };

    [Fact]
    public void CarriesTheConsumerGroupThatDeadLetteredIt()
    {
        var dlq = KafkaEventBus.BuildDeadLetterMessage(Original(), "Tnzi.Events.Probe", "Probe", "Tnzi.EventBus.orders.Probe");

        HeaderOf(dlq, "x-dead-letter-consumer-group").ShouldBe("Tnzi.EventBus.orders.Probe");
    }

    [Fact]
    public void KeepsTheOriginalPayloadHeadersAndSourceMetadata()
    {
        var dlq = KafkaEventBus.BuildDeadLetterMessage(Original(), "Tnzi.Events.Probe", "Probe", "group");

        dlq.Key.ShouldBe("k1");
        dlq.Value.ShouldBe("{\"id\":1}");
        HeaderOf(dlq, "x-original").ShouldBe("keep");
        HeaderOf(dlq, "x-dead-letter-source-topic").ShouldBe("Tnzi.Events.Probe");
        HeaderOf(dlq, "x-dead-letter-event-type").ShouldBe("Probe");
    }

    [Fact]
    public void ToleratesAnOriginalWithoutHeaders()
    {
        var dlq = KafkaEventBus.BuildDeadLetterMessage(new Message<string, string> { Key = "k", Value = "v" }, "t", "e", "g");

        HeaderOf(dlq, "x-dead-letter-consumer-group").ShouldBe("g");
    }

    private static string HeaderOf(Message<string, string> message, string key)
    {
        message.Headers.TryGetLastBytes(key, out var bytes).ShouldBeTrue($"header {key} missing");
        return Encoding.UTF8.GetString(bytes);
    }
}
