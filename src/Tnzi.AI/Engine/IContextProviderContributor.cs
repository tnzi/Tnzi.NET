
namespace Tnzi.AI.Engine;

/// <summary>
/// 上下文提供器贡献者 — 各模块实现此接口注册自己的 IContextProvider
/// </summary>
/// <remarks>
/// <para>
/// 通过 DI 注册 <see cref="IContextProviderContributor"/> 实现，
/// <see cref="AgentExecutorOptionsBuilder"/> 在构建 <see cref="CompositeContextProvider"/> 时
/// 自动收集所有贡献者，取代之前硬编码 new 的方式。
/// </para>
/// <para>
/// 每个贡献者的 <see cref="Order"/> 决定其在组合提供器中的顺序（值越小越先执行）。
/// <see cref="TryCreate"/> 返回 null 表示当前配置不需要此 provider。
/// </para>
/// </remarks>
public interface IContextProviderContributor
{
    /// <summary>
    /// 执行顺序（值越小越先注册到 CompositeContextProvider）
    /// </summary>
    int Order { get; }

    /// <summary>
    /// 尝试创建 IContextProvider 实例
    /// </summary>
    /// <param name="context">创建上下文，包含 agentId、agentName 等运行时参数</param>
    /// <returns>创建成功返回 IContextProvider 实例，否则返回 null</returns>
    IContextProvider? TryCreate(ContextProviderCreationContext context);
}

/// <summary>
/// ContextProvider 创建上下文 — 传递运行时参数给 contributor
/// </summary>
public sealed class ContextProviderCreationContext
{
    /// <summary>
    /// 当前 Agent ID（可选）
    /// </summary>
    public Guid? AgentId { get; init; }

    /// <summary>
    /// 当前 Agent 名称（可选）
    /// </summary>
    public string? AgentName { get; init; }

    /// <summary>
    /// 当前用户 ID（可选）
    /// </summary>
    public Guid? UserId { get; init; }
}
