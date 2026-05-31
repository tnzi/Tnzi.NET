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
