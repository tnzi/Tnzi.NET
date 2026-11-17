namespace Tnzi.AI.Metadata;

/// <summary>
/// 线程里一条排队消息（<see cref="Entities.QueuedThreadMessage"/>）此刻的处境。
/// </summary>
/// <remarks>
/// 作为新一轮发出的消息这一行就删掉：发出的消息从此是线程里的一条用户消息。交给正在跑的进程的消息经过
/// <see cref="HandedOver"/> 与 <see cref="Delivered"/>，在收下它的那一轮结束时删掉。
/// </remarks>
public enum QueuedMessageState
{
    /// <summary>等它所等的那一轮结束（<c>AfterRunId</c>），或者马上就要发（没有在跑的一轮）。</summary>
    Queued = 0,

    /// <summary>正在作为下一轮发出：此刻不能改、不能删。</summary>
    Delivering = 1,

    /// <summary>
    /// 扣住：不会自己发出，只有用户点「立即发送」才单独发出。它等的那一轮被停下（停下的意思是「不要这样继续」），
    /// 或者发出时被拒（<c>Failure</c> 写着原因），或者发出途中服务中断、不知道发没发出去。
    /// </summary>
    Held = 2,

    /// <summary>
    /// 已经写进正在跑的那一轮的 CLI 进程（<c>HandedToRunId</c>），等 agent 在下一步之间收下：此刻不能改、不能删。
    /// 进程没收下就结束了（被停下、失败），它回到 <see cref="Queued"/> 作为新一轮发出，被停下时 <see cref="Held"/>。
    /// </summary>
    HandedOver = 3,

    /// <summary>
    /// agent 收下了：它已经是线程里的一条用户消息（<c>ThreadMessageId</c>），这一轮接着按它做。收下它的那一轮结束时这一行删掉。
    /// </summary>
    Delivered = 4
}
