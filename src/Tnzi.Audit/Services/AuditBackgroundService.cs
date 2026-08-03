
namespace Tnzi.Audit.Services;

/// <summary>
/// 审计日志后台处理服务
/// </summary>
public class AuditBackgroundService : ChannelBatchProcessorBase<AuditOperation>
{
    private readonly IOptionsMonitor<AuditOptions> _options;

    public AuditBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<AuditBackgroundService> logger,
        IAuditConsumer auditConsumer,
        IOptionsMonitor<AuditOptions> options)
        : base(Check.NotNull(auditConsumer).Reader, serviceProvider, logger)
    {
        _options = Check.NotNull(options);
    }

    /// <summary>
    /// 每批读取时取最新配置（BatchSize 随配置中心热更新生效）。
    /// </summary>
    protected override int BatchSize => _options.CurrentValue.BatchSize;

    /// <inheritdoc />
    protected override async Task ProcessBatchAsync(
        IReadOnlyList<AuditOperation> batch,
        IServiceProvider scopedServices,
        CancellationToken cancellationToken)
    {
        var auditStore = scopedServices.GetRequiredService<IAuditStore>();
        await auditStore.SaveOperationBatchAsync(batch.ToList());
    }
}
