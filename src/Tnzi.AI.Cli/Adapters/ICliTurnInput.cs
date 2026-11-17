namespace Tnzi.AI.Cli.Adapters;

/// <summary>
/// 一个协议适配器能在一轮进行中把一条用户消息交给 CLI：CLI 在 agent 的两步之间收下它、并进这一轮，agent 据此调整而不停下
/// （stream-json：claude 在 stdin 上收带 <c>uuid</c> 与 <c>priority: "next"</c> 的用户帧，经 <c>command_lifecycle</c> 报告它的着落）。
/// </summary>
/// <remarks>
/// <para>
/// 收下的那一刻适配器产出 <see cref="CliAgentEventType.UserMessage"/>（<see cref="CliUserMessage.Taken"/> 为 true）：之前的事件属于
/// 收下它之前的那一段回答，之后的属于对它的回答。CLI 明确不收时产出同一类事件、<c>Taken</c> 为 false。写进去了却还没收下时这一轮结束了
/// （消息恰好在最后一段回答时到达），CLI 为它另开一轮：适配器产出 <see cref="CliAgentEventType.TurnCompleted"/> 并接着读，不把那条 result 当成终止信号。
/// </para>
/// <para>没有这个能力的协议（ACP）只能在一轮结束之后作为新的一轮发。</para>
/// </remarks>
public interface ICliTurnInput
{
    /// <summary>把一条用户消息写给正在进行的这一轮。</summary>
    /// <returns>
    /// false：此刻不收，什么都没写（这一轮的提示还没写出、读取已经结束、或者正停在两轮之间等后台任务，那时它该作为新的一轮交给同一个进程）。
    /// </returns>
    Task<bool> SendUserMessageAsync(CliTurnInputMessage message, CancellationToken cancellationToken);
}

/// <summary>交给正在进行的一轮的一条用户消息。</summary>
/// <param name="Id">消息 id（线程消息队列里的排队消息 id）；交给 CLI 时作为它的 uuid，CLI 报告着落时据此认出它。</param>
/// <param name="Text">正文。</param>
/// <param name="Attachments">随它交给模型的附件（图片、PDF）。</param>
public sealed record CliTurnInputMessage(Guid Id, string Text, IReadOnlyList<CliPromptAttachment> Attachments);
