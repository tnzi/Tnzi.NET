namespace Tnzi.Kafka;

/// <summary>
/// 消费一条 Kafka 消息、其处理器执行完毕后的处置决策。
/// </summary>
public enum KafkaConsumeOutcome
{
    /// <summary>所有处理器成功：提交偏移量。</summary>
    Commit,

    /// <summary>有处理器失败且仍在重试预算内：在进程内重试。</summary>
    Retry,

    /// <summary>重试耗尽且启用死信：投递到 DLQ 主题后提交。</summary>
    DeadLetter,

    /// <summary>重试耗尽且未启用死信：不提交偏移量，等待重投（at-least-once，绝不静默丢弃）。</summary>
    RedeliverWithoutCommit,

    /// <summary>
    /// 广播事件重投一次后仍失败：提交偏移量并丢弃。只对广播出现 —— 广播不进死信（过期的广播重放出来是有害的），
    /// 也不保留偏移量等重投（广播组按实例、随实例退出而废弃，等的是一个永远不会回来的消费者）。
    /// </summary>
    Drop
}

/// <summary>
/// Kafka 消费处置策略（纯函数，便于单测）。
/// 修复 "处理器失败仍无条件提交偏移量 → 静默丢消息" 的可靠性缺陷：
/// 失败时不再无条件提交，而是按重试预算重试，耗尽后进 DLQ 或保留偏移量等待重投。
/// </summary>
public static class KafkaConsumeDecider
{
    /// <summary>
    /// 根据处理器失败数、已尝试次数与配置，决定下一步处置。
    /// </summary>
    /// <param name="failureCount">本轮失败的处理器数量（0 表示全部成功）。</param>
    /// <param name="attemptsMade">已完成的处理尝试次数（首轮后为 1）。</param>
    /// <param name="maxRetries">允许的额外重试次数。</param>
    /// <param name="deadLetterEnabled">是否启用死信投递。</param>
    /// <param name="isBroadcast">
    /// 是否广播订阅（<c>IBroadcastIntegrationEvent</c>）。广播与 RabbitMQ 侧同一契约：无论重试预算与死信开关，
    /// 只重试一次，再失败即 <see cref="KafkaConsumeOutcome.Drop"/>。
    /// </param>
    public static KafkaConsumeOutcome Decide(int failureCount, int attemptsMade, int maxRetries, bool deadLetterEnabled, bool isBroadcast = false)
    {
        if (failureCount <= 0)
        {
            return KafkaConsumeOutcome.Commit;
        }

        if (isBroadcast)
        {
            return attemptsMade <= 1 ? KafkaConsumeOutcome.Retry : KafkaConsumeOutcome.Drop;
        }

        if (attemptsMade <= maxRetries)
        {
            return KafkaConsumeOutcome.Retry;
        }

        return deadLetterEnabled
            ? KafkaConsumeOutcome.DeadLetter
            : KafkaConsumeOutcome.RedeliverWithoutCommit;
    }
}
