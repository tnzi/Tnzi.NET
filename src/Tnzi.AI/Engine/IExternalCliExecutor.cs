namespace Tnzi.AI.Engine;

/// <summary>
/// ExternalCli 执行器接口 — 由 Tnzi.AI.Cli 模块实现。
/// 定义在 Tnzi.AI 以便 AgentRuntime 无需引用 Tnzi.AI.Cli。
/// </summary>
public interface IExternalCliExecutor
{
    /// <summary>
    /// 执行 CLI Agent（非流式）
    /// </summary>
    Task<AgentRunResult> ExecuteCliAsync(AiMiddlewareContext context, CancellationToken ct);

    /// <summary>
    /// 执行 CLI Agent（流式）
    /// </summary>
    IAsyncEnumerable<AgentStreamChunk> ExecuteCliStreamingAsync(AiMiddlewareContext context, CancellationToken ct);
}
