namespace Tnzi.AI.Services;

/// <summary>
/// Singleton registry that maps spawned background run IDs to their <see cref="CancellationTokenSource"/>.
/// Allows <see cref="IAgentRunSignalDispatcher.CancelAsync"/> to actually cancel the
/// in-process background task rather than only flipping a DB status flag.
/// </summary>
/// <remarks>
/// 注册表同时记下<b>为什么</b>取消：kill 与超时走的是同一个 CTS、同一条 <see cref="OperationCanceledException"/>，
/// 收尾的 <c>AgentRuntime</c> 只凭令牌分不清两者，而它们的收尾状态不同 —— kill 是用户的决定（Cancelled、不可续跑），
/// 超时是运行没干完（Failed、可续跑）。<see cref="TryCancel"/> 先记原因再触发；超时由注册表自己的计时器
/// 经同一条路走，所以到 <see cref="GetCancellation"/> 那里两种都有据可查。
/// </remarks>
public interface ISubAgentRunCancellationRegistry
{
    /// <summary>
    /// Registers a CTS for the given <paramref name="runId"/>. When <paramref name="timeout"/> is given the registry
    /// cancels the CTS itself once it elapses and records the cancellation as <see cref="RunCancellationKind.TimedOut"/>.
    /// The CTS lifetime is managed by the caller - it must call <see cref="Unregister"/>
    /// (typically in a <c>finally</c> block) when the run finishes.
    /// </summary>
    void Register(Guid runId, CancellationTokenSource cts, TimeSpan? timeout = null);

    /// <summary>
    /// Removes the CTS (and any recorded cancellation) for the given <paramref name="runId"/> and returns it so the
    /// caller can dispose it. Returns <see langword="null"/> if not found.
    /// </summary>
    CancellationTokenSource? Unregister(Guid runId);

    /// <summary>
    /// Cancels the CTS for the given <paramref name="runId"/> if one is registered, recording
    /// <paramref name="reason"/> as a <see cref="RunCancellationKind.Killed"/> cancellation first.
    /// Returns <see langword="true"/> when a live CTS was found and cancelled.
    /// </summary>
    bool TryCancel(Guid runId, string reason);

    /// <summary>
    /// Why the run was cancelled, or <see langword="null"/> when it is not registered or has not been cancelled
    /// through this registry (a caller-owned token, for instance).
    /// </summary>
    RunCancellation? GetCancellation(Guid runId);
}
