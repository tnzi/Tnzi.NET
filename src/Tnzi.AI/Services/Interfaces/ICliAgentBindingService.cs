namespace Tnzi.AI.Services;

/// <summary>
/// Agent 与外部运行时的绑定查询与维护。<b>绑定存在 = 该 Agent 走外部执行。</b>
/// </summary>
/// <remarks>
/// 刻意<b>不改核心 <c>Agent</c> 实体</b>：外部执行是可选子模块的能力，往核心实体上加列
/// 会让每个消费应用都为一个自己不用的功能补迁移。绑定表自己表达「走不走外部」这件事。
/// </remarks>
public interface ICliAgentBindingService
{
    /// <summary>
    /// 取某个 Agent 的绑定。<b>无绑定返回 <c>null</c>，不是错误。</b>
    /// </summary>
    /// <remarks>
    /// 这是 <see cref="IAgentDispatchFacade"/> 的路由判据，因此在**未加载子模块**时
    /// NoOp 实现同样返回 <c>null</c>（而不是 501）—— 没装外部执行能力，就等于所有 Agent
    /// 都走内建路径，这是正确答案而不是失败。写操作才返回 501。
    /// </remarks>
    Task<CliAgentBindingDto?> GetByAgentIdAsync(
        Guid agentId, CancellationToken cancellationToken = default);

    /// <summary>新建或更新绑定。</summary>
    Task<Result<CliAgentBindingDto>> UpsertAsync(
        Guid agentId, UpsertCliAgentBindingDto input, CancellationToken cancellationToken = default);

    /// <summary>解除绑定 —— 该 Agent 回到内建执行。</summary>
    Task<Result> DeleteAsync(Guid agentId, CancellationToken cancellationToken = default);
}
