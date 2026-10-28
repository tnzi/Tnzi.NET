namespace Tnzi.AI.Channels.Store;

/// <summary>
/// 映射里存着的线程被运行拒绝时的判定与自愈。
/// </summary>
/// <remarks>
/// 映射只在「这个会话还没有线程」时写入，之后一直原样读出。它指向的线程可能已经用不了：
/// 管理员删了那条线程，或者绑定规则 / <c>AI:Channels:DefaultAgentId</c> 改了、这个会话如今解析到另一个 Agent
///（线程归属旧 Agent，<c>AgentThreadService</c> 以 <see cref="ErrorCodes.ThreadNotFound"/> 拒绝）。
/// 不自愈的话，这个会话此后的每一条消息都只得到一句通用错误，唯一的出路是 <c>/new</c>，
/// 而 Slack 客户端会把开头的 <c>/</c> 截成斜杠命令、Discord 要消费方注册过的交互，那些会话就永远卡住。
/// <para>
/// 线程解析在管线最前段（模型调用之前）完成，因此被拒的那一次没有跑过任何模型回合，重试一次不会重复计费。
/// 只对 <see cref="ErrorCodes.ThreadNotFound"/> 重试：别的失败（Agent 不存在、配额、模型错误）换一条线程也治不好。
/// </para>
/// </remarks>
internal static class StaleThreadMapping
{
    /// <summary>运行是否以「线程不存在 / 不归这次运行」拒绝了请求里的线程。</summary>
    public static bool IsRejected(Exception exception)
        => exception is BusinessException { Code: ErrorCodes.ThreadNotFound };

    /// <summary>删掉指向被拒线程的映射，让这个会话的下一次运行新建线程并重新登记。</summary>
    public static async Task ForgetAsync(
        IChannelThreadStore threadStore, string channel, string chatId, string? topicId, Guid rejectedThreadId, ILogger logger)
    {
        logger.LogWarning(
            "Channel thread mapping {Channel}:{ChatId} pointed at thread {ThreadId}, which the run rejected (deleted, or bound to another agent); starting a new thread",
            channel, chatId, rejectedThreadId);

        await threadStore.RemoveAsync(channel, chatId, topicId);
    }
}
