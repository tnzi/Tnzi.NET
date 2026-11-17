namespace Tnzi.AI.Cli.Adapters;

/// <summary>
/// 一个协议适配器能把下一条运行的那一轮交给一个仍开着、正在等后台任务的进程（stream-json，文档 §6.18）。
/// </summary>
/// <remarks>
/// <para>
/// 一轮结束而 CLI 还有后台任务时，进程不结束、运行在等（<see cref="IsAwaitingFollowUp"/>）。用户这时发来的下一条消息不排在等待后面：
/// 等着的那条运行到此结束（<see cref="CompleteRunSegment"/> 给出它到这里为止的结果），进程连同后台任务交给下一条运行，
/// 下一条运行把自己的用户帧写进同一个 stdin（<see cref="ContinueAsync"/>），接着读同一条输出流。
/// </para>
/// <para>
/// 同一个适配器实例在两条运行之间交接：协议状态（会话、后台任务、等待的上限）留着，按运行计的东西（用量、记录、结局）从交接处清零。
/// </para>
/// </remarks>
public interface ICliTurnContinuation
{
    /// <summary>
    /// 一轮已经结束、CLI 还有后台任务在跑、下一轮还没开始：此刻没有一轮在进行，进程可以交给下一条运行。
    /// </summary>
    bool IsAwaitingFollowUp { get; }

    /// <summary>
    /// 交接时等着的那条运行到此为止的结果（与 <see cref="ICliProtocolAdapter.GetResult"/> 同一套判定），随后把按运行计的状态清零。
    /// </summary>
    /// <param name="outcome">那条运行的这一段：进程没有退出（<c>ExitCode</c> 为 null）。</param>
    CliAgentResult CompleteRunSegment(CliSessionOutcome outcome);

    /// <summary>
    /// 把下一条运行的用户帧写进还开着的 stdin，这一轮的输出之后从同一条事件流里读到。
    /// </summary>
    /// <returns>false：读取已经结束（CLI 的最后一轮已经收尾、stdin 已关，或等待已到上限），什么都没写，要另起进程。</returns>
    Task<bool> ContinueAsync(CliAgentLaunchContext context, CancellationToken cancellationToken);
}
