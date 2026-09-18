namespace Tnzi.Kafka.Tests;

/// <summary>
/// Kafka 消费处置策略测试。
/// 核心不变量：处理器失败时绝不会走到 "提交偏移量并丢弃消息" 的路径。
/// </summary>
public class KafkaConsumeDeciderTests
{
    [Fact]
    public void AllHandlersSucceed_Commits()
    {
        KafkaConsumeDecider.Decide(failureCount: 0, attemptsMade: 1, maxRetries: 3, deadLetterEnabled: true)
            .ShouldBe(KafkaConsumeOutcome.Commit);
    }

    [Fact]
    public void Success_AlwaysCommits_RegardlessOfAttempts()
    {
        KafkaConsumeDecider.Decide(failureCount: 0, attemptsMade: 99, maxRetries: 3, deadLetterEnabled: false)
            .ShouldBe(KafkaConsumeOutcome.Commit);
    }

    [Fact]
    public void Failure_WithinRetryBudget_Retries()
    {
        KafkaConsumeDecider.Decide(failureCount: 2, attemptsMade: 1, maxRetries: 3, deadLetterEnabled: true)
            .ShouldBe(KafkaConsumeOutcome.Retry);
        KafkaConsumeDecider.Decide(failureCount: 1, attemptsMade: 3, maxRetries: 3, deadLetterEnabled: true)
            .ShouldBe(KafkaConsumeOutcome.Retry);
    }

    [Fact]
    public void Failure_RetriesExhausted_DeadLetters_WhenEnabled()
    {
        KafkaConsumeDecider.Decide(failureCount: 2, attemptsMade: 4, maxRetries: 3, deadLetterEnabled: true)
            .ShouldBe(KafkaConsumeOutcome.DeadLetter);
        // maxRetries=0 ⇒ 首轮失败即耗尽
        KafkaConsumeDecider.Decide(failureCount: 2, attemptsMade: 1, maxRetries: 0, deadLetterEnabled: true)
            .ShouldBe(KafkaConsumeOutcome.DeadLetter);
    }

    /// <summary>
    /// 广播：与 RabbitMQ 侧同一契约 —— 只重试一次、绝不进死信、绝不为一条过期的广播保留偏移量等重投。
    /// </summary>
    /// <remarks>
    /// <c>IEventBus</c> 的契约文本是传输中立的（「处理失败也只在原队列上重投一次而不进死信」），
    /// 而 Kafka 消费循环此前对广播组不做任何区分：按 <c>MaxConsumeRetries</c> 重试、耗尽后照样投 <c>{topic}.dlq</c>。
    /// 广播组按实例、随实例退出而废弃，「不提交偏移量等重投」在这里等于「留给一个永远不会再启动的组」。
    /// </remarks>
    [Fact]
    public void Broadcast_FirstFailure_RetriesOnce_RegardlessOfBudget()
    {
        KafkaConsumeDecider.Decide(failureCount: 1, attemptsMade: 1, maxRetries: 0, deadLetterEnabled: true, isBroadcast: true)
            .ShouldBe(KafkaConsumeOutcome.Retry);
        KafkaConsumeDecider.Decide(failureCount: 1, attemptsMade: 1, maxRetries: 5, deadLetterEnabled: true, isBroadcast: true)
            .ShouldBe(KafkaConsumeOutcome.Retry);
    }

    [Fact]
    public void Broadcast_SecondFailure_Drops_NeverDeadLetters_NeverHoldsTheOffset()
    {
        KafkaConsumeDecider.Decide(failureCount: 1, attemptsMade: 2, maxRetries: 5, deadLetterEnabled: true, isBroadcast: true)
            .ShouldBe(KafkaConsumeOutcome.Drop);
        KafkaConsumeDecider.Decide(failureCount: 1, attemptsMade: 2, maxRetries: 5, deadLetterEnabled: false, isBroadcast: true)
            .ShouldBe(KafkaConsumeOutcome.Drop);
    }

    [Fact]
    public void Broadcast_Success_Commits()
    {
        KafkaConsumeDecider.Decide(failureCount: 0, attemptsMade: 1, maxRetries: 3, deadLetterEnabled: true, isBroadcast: true)
            .ShouldBe(KafkaConsumeOutcome.Commit);
    }

    [Fact]
    public void WorkQueue_NeverDrops()
    {
        foreach (var attempts in Enumerable.Range(1, 6))
        {
            KafkaConsumeDecider.Decide(failureCount: 1, attemptsMade: attempts, maxRetries: 3, deadLetterEnabled: true, isBroadcast: false)
                .ShouldNotBe(KafkaConsumeOutcome.Drop);
            KafkaConsumeDecider.Decide(failureCount: 1, attemptsMade: attempts, maxRetries: 3, deadLetterEnabled: false, isBroadcast: false)
                .ShouldNotBe(KafkaConsumeOutcome.Drop);
        }
    }

    [Fact]
    public void Failure_RetriesExhausted_RedeliversWithoutCommit_WhenDlqDisabled()
    {
        // 关键不变量：DLQ 关闭且重试耗尽 ⇒ 不提交偏移量（绝不静默丢消息）
        KafkaConsumeDecider.Decide(failureCount: 2, attemptsMade: 4, maxRetries: 3, deadLetterEnabled: false)
            .ShouldBe(KafkaConsumeOutcome.RedeliverWithoutCommit);
        KafkaConsumeDecider.Decide(failureCount: 2, attemptsMade: 1, maxRetries: 0, deadLetterEnabled: false)
            .ShouldBe(KafkaConsumeOutcome.RedeliverWithoutCommit);
    }
}
