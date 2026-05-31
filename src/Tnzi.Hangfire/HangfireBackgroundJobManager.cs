namespace Tnzi.Hangfire;

/// <summary>
/// Hangfire 后台任务管理器适配
/// 注意：表达式中的 default (CancellationToken.None) 是 Hangfire 表达式树 API 的设计约束。
/// Hangfire 在执行时通过自己的 IJobCancellationToken 机制提供取消支持，
/// 这里的 CancellationToken 只是占位符，不会在实际执行时使用。
/// </summary>
public class HangfireBackgroundJobManager : IBackgroundJobManager
{
    private readonly IServiceProvider _serviceProvider;

    public HangfireBackgroundJobManager(IServiceProvider serviceProvider)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
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
    /// 注意：ICurrentTenant 是 Scoped 服务，不能在 Singleton 构造时持有引用；
    /// 通过根 IServiceProvider 创建 scope 动态解析，避免 captive dependency。
    /// 与 LocalEventBus 中 PublishAsync 的处理方式一致。
    /// </summary>
    private void CaptureTenantContext<TArgs>(TArgs args) where TArgs : class
    {
        if (args is not ITenantAwareJobArgs tenantAware || tenantAware.TenantId != null)
        {
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var currentTenant = scope.ServiceProvider.GetService<ICurrentTenant>();
        tenantAware.TenantId = currentTenant?.Id;
    }
}
