namespace Tnzi.AI.Services;

/// <summary>
/// Run 持久化存储接口
/// </summary>
public interface IRunStore
{
    /// <summary>创建 Run 记录</summary>
    Task<AgentRun> CreateAsync(AgentRun run, CancellationToken cancellationToken = default);

    /// <summary>更新 Run 状态</summary>
    Task UpdateAsync(AgentRun run, CancellationToken cancellationToken = default);

    /// <summary>获取 Run</summary>
    Task<AgentRun?> GetAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>获取 Run（含节点）</summary>
    Task<AgentRun?> GetWithNodesAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>添加节点记录</summary>
    Task<AgentRunNode> AddNodeAsync(AgentRunNode node, CancellationToken cancellationToken = default);

    /// <summary>更新节点状态</summary>
    Task UpdateNodeAsync(AgentRunNode node, CancellationToken cancellationToken = default);

    /// <summary>按条件分页查询 Run 列表</summary>
    Task<List<AgentRun>> ListAsync(AgentRunStatus? status, int maxResults, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按归属人（<c>AgentRun.CreatorId</c>）列出 Run（按创建时间倒序）。
    /// <paramref name="ownerUserId"/> 为 null 时只返回无主的运行（未知调用方只看得见同样无主的运行）。
    /// </summary>
    Task<List<AgentRun>> ListByOwnerAsync(Guid? ownerUserId, AgentRunStatus? status, int maxResults, CancellationToken cancellationToken = default);

    /// <summary>统计指定根 Run 下的后代数量（不含根自身）</summary>
    Task<int> CountDescendantsAsync(Guid rootRunId, CancellationToken cancellationToken = default);

    /// <summary>统计指定根 Run 下仍在跑（Pending / Running）的后代数量（不含根自身）—— 树内并发上限的判据</summary>
    Task<int> CountActiveDescendantsAsync(Guid rootRunId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 统计归属人（<c>AgentRun.CreatorId</c>）名下仍在跑（Pending / Running）的<b>根</b> Run 数量（无父运行）
    /// —— 顶层 spawn 的并发上限判据。<paramref name="ownerUserId"/> 为 null 时只数无主的运行
    /// （与 <see cref="ListByOwnerAsync"/> 同口径：认不出调用者不等于不限）。
    /// </summary>
    Task<int> CountActiveRootRunsByOwnerAsync(Guid? ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>获取指定 Run 的父 Run ID（仅 Id + ParentRunId 字段）</summary>
    Task<Guid?> GetParentRunIdAsync(Guid runId, CancellationToken cancellationToken = default);
}
