namespace Tnzi.AI.Cli.Services;

/// <summary>
/// 一个 CLI 进程此刻属于哪条运行，按起它的那条运行查（文档 §6.18「等待期间的下一条消息」）。
/// </summary>
/// <remarks>
/// <para>
/// 消费方常把按运行签的东西放进进程的启动环境（模型网关的令牌、用量归属），绑在起进程的那条运行上。等后台任务的进程交给同线程的下一条运行之后，
/// 起它的那条已经结束，进程却还在为下一条运行调模型：只认起进程那条运行的网关会拒绝它的每一个请求，用量也记错人。
/// 这里按起进程的运行 id 答出此刻拥有进程的运行：它自己还在跑时是它自己，交出去之后是接手的那条（一路交接下去是最后接手的那条），
/// 拥有进程的运行结束了就答 null，网关据此放行、拒绝并记账。
/// </para>
/// <para>
/// 答案来自运行记录（<c>CliRun.ProcessLaunchedByRunId</c>，接手的运行在新提示写进进程之前写下），任一副本、任何时候查都一样。
/// 跨租户查：网关的请求没有当前租户，起进程的运行 id 本身就是范围（与回写凭据的校验同一做法）。
/// </para>
/// </remarks>
public interface ICliProcessOwnership
{
    /// <summary>
    /// 由 <paramref name="launchedByRunId"/> 起的进程此刻属于哪条运行；那条运行不在进行中（Dispatched / Running）时返回 null。
    /// </summary>
    Task<CliProcessOwner?> GetCurrentOwnerAsync(Guid launchedByRunId, CancellationToken cancellationToken = default);
}

/// <summary>此刻拥有进程的运行。</summary>
/// <param name="RunId">拥有进程的运行（模型调用、用量归它）。</param>
/// <param name="LaunchedByRunId">起进程的运行（进程环境绑在它上面）；运行自己起的进程时等于 <paramref name="RunId"/>。</param>
/// <param name="AgentId">它的 Agent。</param>
/// <param name="ThreadId">它的线程。</param>
/// <param name="UserId">它归属的用户（<c>CreatorId</c>）。</param>
/// <param name="TenantId">它的租户。</param>
/// <param name="Status">它的状态（Dispatched 或 Running）。</param>
public sealed record CliProcessOwner(
    Guid RunId, Guid LaunchedByRunId, Guid AgentId, Guid? ThreadId, Guid? UserId, Guid? TenantId, CliRunStatus Status);

/// <inheritdoc />
public class CliProcessOwnership : ICliProcessOwnership
{
    private readonly IRepository<CliRun, Guid> _runs;

    /// <summary>初始化。</summary>
    public CliProcessOwnership(IRepository<CliRun, Guid> runs)
    {
        _runs = Check.NotNull(runs);
    }

    /// <inheritdoc />
    public async Task<CliProcessOwner?> GetCurrentOwnerAsync(Guid launchedByRunId, CancellationToken cancellationToken = default)
    {
        // 读整行而不是投影：多租户关闭时 TenantId 不在模型里，投影它的查询翻译不了。
        // 自己起的进程：它自己（没有链接）；交出去之后：链接指向它的那条。任何时刻至多一条在进行中（交出方先落终态，接手方才写链接）
        var owner = await _runs.AcrossTenants()
            .Where(r => (r.Id == launchedByRunId && r.ProcessLaunchedByRunId == null) || r.ProcessLaunchedByRunId == launchedByRunId)
            .Where(r => r.Status == CliRunStatus.Dispatched || r.Status == CliRunStatus.Running)
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return owner is null
            ? null
            : new CliProcessOwner(owner.Id, launchedByRunId, owner.AgentId, owner.ThreadId, owner.CreatorId, owner.TenantId, owner.Status);
    }
}
