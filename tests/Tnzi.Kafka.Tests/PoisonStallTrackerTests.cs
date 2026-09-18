using Confluent.Kafka;

namespace Tnzi.Kafka.Tests;

/// <summary>
/// 毒消息卡住周期按<b>分区</b>计数：另一个分区送来一条好记录不能把它清零。
/// </summary>
/// <remarks>
/// 死信关闭时毒消息被 Seek 回原地，每个轮询周期重读一次，Critical 只在第一个周期与之后每 60 个周期各记一条。
/// 此前计数是单个变量，任何一次成功的反序列化都会清掉它 —— 多分区主题上只要别的分区有流量，
/// 卡住的那条每个周期都从 0 重新算起，Critical 每秒一条，节流形同虚设。
/// </remarks>
public class PoisonStallTrackerTests
{
    private static TopicPartitionOffset At(int partition, long offset) => new("t", new Partition(partition), new Offset(offset));

    [Fact]
    public void FirstSightOfAnOffset_IsCycleZero()
    {
        var tracker = new PoisonStallTracker();

        tracker.Register(At(0, 42)).ShouldBe(0);
    }

    [Fact]
    public void ReReadingTheSameOffset_CountsUp()
    {
        var tracker = new PoisonStallTracker();
        tracker.Register(At(0, 42));

        tracker.Register(At(0, 42)).ShouldBe(1);
        tracker.Register(At(0, 42)).ShouldBe(2);
    }

    [Fact]
    public void AGoodRecordOnAnotherPartition_DoesNotResetTheStall()
    {
        var tracker = new PoisonStallTracker();
        tracker.Register(At(0, 42));

        tracker.Clear(new TopicPartition("t", new Partition(1)));

        tracker.Register(At(0, 42)).ShouldBe(1, "partition 1 delivering a good record says nothing about the record stuck on partition 0");
    }

    [Fact]
    public void AGoodRecordOnTheStalledPartition_ResetsIt()
    {
        var tracker = new PoisonStallTracker();
        tracker.Register(At(0, 42));
        tracker.Register(At(0, 42));

        tracker.Clear(new TopicPartition("t", new Partition(0)));

        tracker.Register(At(0, 42)).ShouldBe(0);
    }

    [Fact]
    public void ADifferentOffsetOnTheSamePartition_StartsANewStall()
    {
        var tracker = new PoisonStallTracker();
        tracker.Register(At(0, 42));
        tracker.Register(At(0, 42));

        tracker.Register(At(0, 43)).ShouldBe(0);
    }

    [Fact]
    public void TwoStalledPartitions_AreCountedIndependently()
    {
        var tracker = new PoisonStallTracker();
        tracker.Register(At(0, 42));
        tracker.Register(At(1, 7));

        tracker.Register(At(0, 42)).ShouldBe(1);
        tracker.Register(At(1, 7)).ShouldBe(1);
    }
}
