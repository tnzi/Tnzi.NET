using Tnzi.Data.Snows;

namespace Tnzi.Tests.Data;

/// <summary>
/// 雪花 id 的序号段不能溢进机器码位：同一毫秒里序号用到上限后要换到下一毫秒，而不是再多发一个
/// <c>MaxSeqNumber + 1</c>（等于把机器码加 1、序号归 0，落进相邻机器的号段）。
/// </summary>
public class SnowWorkerSequenceTests
{
    [Theory]
    [InlineData((short)1)]
    [InlineData((short)2)]
    public void EveryId_KeepsTheConfiguredWorkerId_AndASequenceWithinRange(short method)
    {
        var options = new IdGeneratorOptions { WorkerId = 9, Method = method };
        var generator = new DefaultIdGenerator(options);
        var seqMask = (1L << options.SeqBitLength) - 1;
        var workerMask = (1L << options.WorkerIdBitLength) - 1;
        var maxSeq = (int)seqMask;

        var ids = new HashSet<long>();
        for (var i = 0; i < 5000; i++)
        {
            var id = generator.NewLong();
            Assert.True(ids.Add(id), $"duplicate id {id}");

            var workerId = (id >> options.SeqBitLength) & workerMask;
            var sequence = id & seqMask;
            Assert.Equal(options.WorkerId, workerId);
            Assert.InRange(sequence, 0, maxSeq);
        }
    }
}
