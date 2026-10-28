namespace Tnzi.AI.Services;

/// <summary>
/// 在跑的运行定期刷新 <see cref="AgentRun.LastHeartbeatAt"/>，供 <see cref="AgentRunRecoveryService"/> 区分
/// 「还在跑」与「执行它的进程已经没了」。
/// </summary>
public interface IAgentRunHeartbeat
{
    /// <summary>
    /// 为一次运行开始心跳；释放返回值即停止。只刷新仍处于 Pending / Running 的行。
    /// </summary>
    IAsyncDisposable Start(Guid runId);
}
