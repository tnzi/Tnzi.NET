namespace Tnzi.AI.Cli.Services;

/// <summary>
/// 外部运行在等主人的线程：线程上有一条还没结束的运行（排队中、已派发、执行中），它提出的权限 / 问题 / 计划请求
/// 还在等回答且还没过期（<see cref="CliRunPermissionRequest"/>）。过期的请求会按拒绝交给 CLI，不再算在等。
/// </summary>
/// <remarks>
/// 跨租户地查（同调度器的系统查询），不按主人筛：运行可以由应用替主人起，记在运行上的创建者不一定是线程主人。
/// 线程服务只取与它自己的查询（主人、租户、归档）相交的部分。还没了结的请求只有一小撮，查询便宜。
/// </remarks>
internal sealed class CliRunAwaitingOwnerProvider : IThreadAwaitingOwnerProvider
{
    private readonly IRepository<CliRun, Guid> _runs;
    private readonly IRepository<CliRunPermissionRequest, Guid> _requests;

    public CliRunAwaitingOwnerProvider(IRepository<CliRun, Guid> runs, IRepository<CliRunPermissionRequest, Guid> requests)
    {
        _runs = Check.NotNull(runs);
        _requests = Check.NotNull(requests);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<Guid>> GetThreadIdsAsync(ThreadAwaitingOwnerQuery query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var runs = _runs.AcrossTenants()
            .Where(r => r.ThreadId != null
                        && (r.Status == CliRunStatus.Queued
                            || r.Status == CliRunStatus.Dispatched
                            || r.Status == CliRunStatus.Running));
        if (query.ThreadIds is not null)
        {
            var ids = query.ThreadIds.Distinct().ToList();
            runs = runs.Where(r => ids.Contains(r.ThreadId!.Value));
        }

        var now = DateTime.UtcNow;
        var requests = _requests.AsQueryable()
            .IgnoreQueryFilters()
            .Where(p => p.Status == CliPermissionStatus.Pending && p.ExpiresAt > now);

        return await requests
            .Join(runs, p => p.RunId, r => r.Id, (p, r) => r.ThreadId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
    }
}
