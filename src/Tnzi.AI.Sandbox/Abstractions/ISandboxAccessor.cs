namespace Tnzi.AI.Sandbox.Abstractions;

/// <summary>
/// 显式获取当前 Agent 运行的活动沙箱 — 取代消费者直接读 AsyncLocal 通道
/// (<c>IAgentExecutionContextAccessor.Properties[SandboxPropertyKeys.ToolEnvironment]</c>)。
/// 仅在 <c>SandboxMiddleware</c> 的 next() 执行期间有值；之外返回 null。
/// 自定义工具/服务注入本接口即可拿到当前沙箱，无需理解 AsyncLocal 传播链。
/// </summary>
public interface ISandboxAccessor
{
    /// <summary>当前活动沙箱实例（无活动沙箱时为 null）。</summary>
    ISandbox? Current { get; }

    /// <summary>当前沙箱环境（含 Sandbox + ThreadId；无活动沙箱时为 null）。</summary>
    SandboxToolEnvironment? CurrentEnvironment { get; }
}
