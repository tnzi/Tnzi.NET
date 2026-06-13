namespace Tnzi.AI.Sandbox.Models;

public static class SandboxPropertyKeys
{
    /// <summary>
    /// AiMiddlewareContext.Properties 键：<see cref="ThreadDataState"/>。
    /// 由 ThreadDataMiddleware 写入，SandboxMiddleware 消费。
    /// </summary>
    public const string ThreadData = "ThreadData";

    /// <summary>
    /// IAgentExecutionContextAccessor.Properties 键：<see cref="SandboxToolEnvironment"/>。
    /// 由 SandboxMiddleware 在 next() 执行期间写入（finally 移除），
    /// SandboxTools 在工具调用时读取。环境缺失时工具返回结构化错误。
    /// </summary>
    public const string ToolEnvironment = "SandboxToolEnvironment";
}
