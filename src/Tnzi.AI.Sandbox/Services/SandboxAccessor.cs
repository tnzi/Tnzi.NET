
namespace Tnzi.AI.Sandbox.Services;

/// <summary>
/// <see cref="ISandboxAccessor"/> 默认实现 - 从 <see cref="IAgentExecutionContextAccessor"/>
/// 的属性包读取 <c>SandboxMiddleware</c> 发布的当前沙箱环境。
/// </summary>
public sealed class SandboxAccessor : ISandboxAccessor
{
    private readonly IAgentExecutionContextAccessor _executionContextAccessor;

    public SandboxAccessor(IAgentExecutionContextAccessor executionContextAccessor)
    {
        _executionContextAccessor = Check.NotNull(executionContextAccessor);
    }

    public SandboxToolEnvironment? CurrentEnvironment =>
        _executionContextAccessor.Properties.GetValueOrDefault(SandboxPropertyKeys.ToolEnvironment) as SandboxToolEnvironment;

    public ISandbox? Current => CurrentEnvironment?.Sandbox;
}
