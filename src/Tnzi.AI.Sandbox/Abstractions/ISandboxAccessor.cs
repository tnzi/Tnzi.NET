namespace Tnzi.AI.Sandbox.Abstractions;

/// <summary>
/// 显式获取当前 Agent 运行的沙箱 - 取代消费者直接读 AsyncLocal 通道
/// (<c>IAgentExecutionContextAccessor.Properties[SandboxPropertyKeys.ToolEnvironment]</c>)。
/// 仅在 <c>SandboxMiddleware</c> 的 next() 执行期间有值；之外返回 null。
/// 自定义工具/服务注入本接口即可拿到当前沙箱，无需理解 AsyncLocal 传播链。
/// </summary>
/// <remarks>
/// ★ 沙箱是<b>按需创建</b>的（2026-09-14 起）：环境在运行开始就发布，沙箱要到第一次
/// <see cref="GetCurrentAsync"/> / 第一个沙箱工具调用才建出来。所以这里没有同步的 <c>Current</c>：
/// 它在沙箱建出来之前只能答 null，而自定义工具读到 null 之后能做的只有报「没有沙箱」，
/// 与一个真的没有环境的运行无法区分。
/// </remarks>
public interface ISandboxAccessor
{
    /// <summary>当前沙箱环境（含 ThreadId 与按需创建的沙箱；无活动环境时为 null）。</summary>
    SandboxToolEnvironment? CurrentEnvironment { get; }

    /// <summary>
    /// 取当前运行的沙箱，还没建就建。无活动环境（中间件未运行 / 请求无线程 / 沙箱禁用）时返回 null；
    /// 有环境但 provider 建不出来时把异常原样抛出。
    /// </summary>
    ValueTask<ISandbox?> GetCurrentAsync(CancellationToken ct = default);
}
