namespace Tnzi.Hangfire;

/// <summary>
/// Hangfire 后台任务管理器适配
/// 注意：表达式中的 default (CancellationToken.None) 是 Hangfire 表达式树 API 的设计约束。
/// Hangfire 在执行时通过自己的 IJobCancellationToken 机制提供取消支持，
/// 这里的 CancellationToken 只是占位符，不会在实际执行时使用。
/// </summary>
public class HangfireBackgroundJobManager : IBackgroundJobManager
{
    private readonly ICurrentTenant? _currentTenant;

    public HangfireBackgroundJobManager(ICurrentTenant? currentTenant = null)
    {
        _currentTenant = currentTenant;
    }

    /// <inheritdoc />
    public string Enqueue<TArgs>(TArgs args) where TArgs : class
    {
        Check.NotNull(args);
        CaptureTenantContext(args);
        return BackgroundJob.Enqueue<IBackgroundJob<TArgs>>(job => job.ExecuteAsync(args, default));
    }

    /// <inheritdoc />
    public string Schedule<TArgs>(TArgs args, TimeSpan delay) where TArgs : class
    {
        Check.NotNull(args);
        CaptureTenantContext(args);
        return BackgroundJob.Schedule<IBackgroundJob<TArgs>>(
            job => job.ExecuteAsync(args, default),
            delay);
    }

    /// <inheritdoc />
    public string Schedule<TArgs>(TArgs args, DateTimeOffset enqueueAt) where TArgs : class
    {
        Check.NotNull(args);
        CaptureTenantContext(args);
        return BackgroundJob.Schedule<IBackgroundJob<TArgs>>(
            job => job.ExecuteAsync(args, default),
            enqueueAt);
    }

    /// <inheritdoc />
    public void CreateRecurring<TArgs>(string jobId, TArgs args, string cronExpression) where TArgs : class
    {
        Check.NotNullOrEmpty(jobId);
        Check.NotNull(args);
        Check.NotNullOrEmpty(cronExpression);
        CaptureTenantContext(args);
        RecurringJob.AddOrUpdate<IBackgroundJob<TArgs>>(
            jobId,
            job => job.ExecuteAsync(args, default),
            cronExpression);
    }

    /// <inheritdoc />
    public bool Delete(string jobId)
    {
        Check.NotNullOrEmpty(jobId);
        return BackgroundJob.Delete(jobId);
    }

    /// <inheritdoc />
    public void DeleteRecurring(string jobId)
    {
        Check.NotNullOrEmpty(jobId);
        RecurringJob.RemoveIfExists(jobId);
    }

    /// <summary>
    /// 自动捕获当前租户上下文到任务参数
    /// </summary>
    private void CaptureTenantContext<TArgs>(TArgs args) where TArgs : class
    {
        if (args is ITenantAwareJobArgs tenantAware && tenantAware.TenantId == null)
        {
            tenantAware.TenantId = _currentTenant?.Id;
        }
    }
}
