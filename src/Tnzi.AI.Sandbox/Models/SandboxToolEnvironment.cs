namespace Tnzi.AI.Sandbox.Models;

/// <summary>
/// 沙箱工具执行环境 — SandboxMiddleware 在 Agent 运行期间通过
/// <c>IAgentExecutionContextAccessor.Properties[SandboxPropertyKeys.ToolEnvironment]</c>
/// 发布给 <see cref="Tools.SandboxTools"/> 的环境参数（AsyncLocal 通道，
/// 主管线与 AgentAsTools 子代理路径均可见）。
/// </summary>
/// <param name="Sandbox">当前运行的活动沙箱实例（由 SandboxMiddleware 创建并负责释放）</param>
/// <param name="ThreadId">沙箱绑定的线程 Id（虚拟路径翻译与线程配额使用同一 Id）</param>
public sealed record SandboxToolEnvironment(ISandbox Sandbox, Guid ThreadId);
