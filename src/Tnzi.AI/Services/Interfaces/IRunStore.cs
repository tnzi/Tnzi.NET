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

    /// <summary>
    /// 读存储里 Run 此刻的状态（行不存在时为 null）。
    /// </summary>
    /// <remarks>
    /// 必须读存储本身，不能读调用方作用域里已跟踪的实体：后台运行在自己的作用域里持有那一行，
    /// 别的作用域（kill_agent / 管理端 cancel）写进去的 Cancelled 只有直接读存储才看得见。
    /// 默认实现经 <see cref="GetAsync"/> 读，对带变更跟踪的存储会读到本作用域里的旧值，这类实现应当覆写。
    /// </remarks>
    async Task<AgentRunStatus?> GetStatusAsync(Guid runId, CancellationToken cancellationToken = default)
        => (await GetAsync(runId, cancellationToken))?.Status;

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

    /// <summary>
    /// 把仍停在 Pending / Running 的 Run 收尾为 <paramref name="status"/>，并写下 <paramref name="error"/>；
    /// 已到终态或处于暂停态（等待审批 / 澄清）的行不动。
    /// </summary>
    /// <returns>这一行是否被改动</returns>
    /// <remarks>
    /// 用在执行者已经结束、却没写下终态的地方（后台运行在建立追踪之前就失败）。必须是条件更新：
    /// 运行自己的收尾先到时以它为准。默认实现经 <see cref="GetAsync"/> / <see cref="UpdateAsync"/> 读改写，不是原子的，
    /// 带数据库的实现应当覆写为一条条件更新。
    /// </remarks>
    async Task<bool> CloseIfStillActiveAsync(Guid runId, AgentRunStatus status, string error, CancellationToken cancellationToken = default)
    {
        var run = await GetAsync(runId, cancellationToken);
        if (run is not { Status: AgentRunStatus.Pending or AgentRunStatus.Running })
        {
            return false;
        }

        run.Status = status;
        run.Error = StringTruncator.Truncate(error, 1000);
        run.LastHeartbeatAt = DateTime.UtcNow;
        await UpdateAsync(run, cancellationToken);
        return true;
    }
}
