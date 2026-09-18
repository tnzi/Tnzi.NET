namespace Tnzi.Kafka;

/// <summary>
/// 按分区记录「毒消息卡住了多少个轮询周期」，供 Critical 日志节流用。
/// </summary>
/// <remarks>
/// 死信关闭时毒消息被 Seek 回原地、每个轮询周期重读一次；Critical 只在第一个周期与之后每 N 个周期各记一条。
/// 计数必须按<b>分区</b>：一个消费者常持有多个分区，另一个分区送来一条好记录说明不了卡住的那条有任何变化。
/// 此前它是单个变量、任何一次成功的反序列化都会清零 —— 多分区主题上只要别的分区有流量，
/// 卡住的那条每个周期都从 0 重新算起，Critical 每秒一条，节流形同虚设。
/// 只在消费循环的单个线程上使用，不做同步。
/// </remarks>
internal sealed class PoisonStallTracker
{
    private readonly Dictionary<TopicPartition, (Offset Offset, int Cycles)> _stalls = new();

    /// <summary>
    /// 登记一次对毒消息的重读，返回它在同一偏移量上已连续卡住的周期数（首次看到为 0）。
    /// 同一分区换了偏移量即视为一条新的毒消息，从 0 重新计。
    /// </summary>
    public int Register(TopicPartitionOffset offset)
    {
        Check.NotNull(offset);

        var cycles = _stalls.TryGetValue(offset.TopicPartition, out var current) && current.Offset == offset.Offset
            ? current.Cycles + 1
            : 0;
        _stalls[offset.TopicPartition] = (offset.Offset, cycles);
        return cycles;
    }

    /// <summary>该分区送来了一条能反序列化的记录：它不再卡住。</summary>
    public void Clear(TopicPartition partition)
    {
        Check.NotNull(partition);
        _stalls.Remove(partition);
    }
}
