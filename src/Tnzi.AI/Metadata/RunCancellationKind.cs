namespace Tnzi.AI.Metadata;

/// <summary>取消的来源：决定运行的收尾状态。</summary>
public enum RunCancellationKind
{
    /// <summary>kill_agent / 管理端 cancel：用户的决定，运行以 Cancelled 收尾。</summary>
    Killed,

    /// <summary><c>AI:SubAgent:TimeoutSeconds</c> 到点：运行没干完，以 Failed 收尾（可续跑）。</summary>
    TimedOut
}
