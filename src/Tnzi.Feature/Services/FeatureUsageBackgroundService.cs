namespace Tnzi.Feature.Services;

/// <summary>
/// 从用量队列成批读出记录，在独立 DI 作用域里批量落库。
/// </summary>
/// <remarks>
/// 记录上的 <c>UserId</c> / <c>TenantId</c> / <c>CreationTime</c> 在<b>入队时</b>已经定格
/// （<see cref="FeatureUsageService.RecordUsageAsync"/>）：后台作用域里没有当前用户也没有当前租户，
/// 而 <c>SaveChanges</c> 的审计填充只补空值、不覆盖已有值，所以这里原样插入即可。
/// 批处理失败由基类记日志并丢弃这一批 —— 遥测数据的既定取舍，见 <c>ChannelBatchProcessorBase</c>。
/// </remarks>
public class FeatureUsageBackgroundService : ChannelBatchProcessorBase<FeatureUsageRecord>
{
    /// <summary>
    /// 初始化用量落库后台服务。
    /// </summary>
    public FeatureUsageBackgroundService(
        IFeatureUsageConsumer consumer,
        IServiceProvider serviceProvider,
        ILogger<FeatureUsageBackgroundService> logger)
        : base(Check.NotNull(consumer).Reader, serviceProvider, logger)
    {
    }

    /// <inheritdoc />
    protected override int BatchSize => 200;

    /// <inheritdoc />
    protected override async Task ProcessBatchAsync(
        IReadOnlyList<FeatureUsageRecord> batch,
        IServiceProvider scopedServices,
        CancellationToken cancellationToken)
    {
        var repository = scopedServices.GetRequiredService<IRepository<FeatureUsageRecord, long>>();
        await repository.InsertManyAsync(batch, cancellationToken);
    }
}
