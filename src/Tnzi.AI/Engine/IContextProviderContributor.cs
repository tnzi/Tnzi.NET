
namespace Tnzi.AI.Engine;

/// <summary>
/// 上下文提供器贡献者 - 各模块实现此接口注册自己的 IContextProvider
/// </summary>
/// <remarks>
/// <para>
/// 通过 DI 注册 <see cref="IContextProviderContributor"/> 实现，
/// <c>CompositeContextProviderFactory</c> 注入所有已注册的贡献者并按 <see cref="Order"/> 排序，
/// 在每次请求时调用 <c>TryBuild</c> 收集各贡献者产出的 provider，组装成一个
/// <see cref="CompositeContextProvider"/>。该工厂由 <c>ContextInjectionMiddleware</c> 驱动。
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
/// ContextProvider 创建上下文 - 传递运行时参数给 contributor
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

    /// <summary>
    /// 该 Agent 分配的知识库 ID 列表（RAG 检索按此范围；空/null = 跨所有启用知识库）
    /// </summary>
    public IReadOnlyList<Guid>? KnowledgeBaseIds { get; init; }

    /// <summary>
    /// 该 Agent 分配的技能 slug 列表（非空时仅这些技能对 Agent 可见）
    /// </summary>
    public IReadOnlyList<string>? SkillSlugs { get; init; }
}
