namespace Tnzi.BackgroundJobs;

/// <summary>
/// 租户感知的后台任务基类
/// 自动在执行前恢复租户上下文，确保后台任务中的数据库查询带有正确的租户过滤
/// </summary>
public abstract class TenantAwareBackgroundJob<TArgs> : IBackgroundJob<TArgs>
    where TArgs : class, ITenantAwareJobArgs
{
    private readonly ICurrentTenant? _currentTenant;

    protected TenantAwareBackgroundJob(ICurrentTenant? currentTenant = null)
    {
        _currentTenant = currentTenant;
    }

    public async Task ExecuteAsync(TArgs args, CancellationToken cancellationToken = default)
    {
        if (_currentTenant != null && args.TenantId.HasValue)
        {
            using (_currentTenant.Change(args.TenantId.Value))
            {
                await ExecuteInTenantContextAsync(args, cancellationToken);
            }
        }
        else
        {
            await ExecuteInTenantContextAsync(args, cancellationToken);
        }
    }

    /// <summary>
    /// 在租户上下文中执行任务（子类实现此方法替代 ExecuteAsync）
    /// </summary>
    protected abstract Task ExecuteInTenantContextAsync(TArgs args, CancellationToken cancellationToken);
}
