
namespace Tnzi.Audit.Services;

/// <summary>
/// 审计日志后台处理服务
/// </summary>
public class AuditBackgroundService : ChannelBatchProcessorBase<AuditOperation>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptionsMonitor<AuditOptions> _options;

    public AuditBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<AuditBackgroundService> logger,
        IAuditConsumer auditConsumer,
        IOptionsMonitor<AuditOptions> options)
        : base(Check.NotNull(auditConsumer).Reader, serviceProvider, logger)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        _options = Check.NotNull(options);
    }

    /// <summary>
    /// 每批读取时取最新配置（BatchSize 随配置中心热更新生效）。
    /// </summary>
    protected override int BatchSize => _options.CurrentValue.BatchSize;

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// ★ 批量 INSERT 是一条语句：一行的列宽 / 约束问题会让 SQL Server / PostgreSQL 拒绝整条语句，
    /// 而基类的既定取舍是「批处理失败记日志后丢弃这一批」。两条规则叠在一起，一行坏数据就带走了
    /// 同一时间窗里其他所有人的审计记录（默认一批 100 条）。所以行级拒绝（<see cref="DbUpdateException"/>）
    /// 之后<b>逐行重试</b>：每行一个干净的 DI 作用域 —— 失败的那个 DbContext 仍跟踪着整批实体图，
    /// 复用它会把上一行的失败一起重放 —— 只有真正坏的那一行被记录并放弃。
    /// </para>
    /// <para>
    /// 其它异常（连接断开、超时）原样抛给基类：那类故障逐行重试只会把一次失败放大成一百次，
    /// 基类的记日志、退避、丢弃正是为它设计的。
    /// </para>
    /// </remarks>
    protected override async Task ProcessBatchAsync(
        IReadOnlyList<AuditOperation> batch,
        IServiceProvider scopedServices,
        CancellationToken cancellationToken)
    {
        var auditStore = scopedServices.GetRequiredService<IAuditStore>();
        try
        {
            await auditStore.SaveOperationBatchAsync(batch.ToList());
            return;
        }
        catch (DbUpdateException ex)
        {
            Logger.LogWarning(ex,
                "{Service}: the database rejected a batch of {Count} audit operations; retrying row by row so that one bad row does not drop the others.",
                ServiceName, batch.Count);
        }

        await SaveRowByRowAsync(batch, cancellationToken);
    }

    private async Task SaveRowByRowAsync(IReadOnlyList<AuditOperation> batch, CancellationToken cancellationToken)
    {
        var dropped = 0;
        foreach (var operation in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var scope = _serviceProvider.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IAuditStore>();
            try
            {
                await store.SaveOperationAsync(operation);
            }
            catch (DbUpdateException ex)
            {
                dropped++;
                // 记下足够认出这一行的信息，但不记它的内容 —— 让它失败的往往就是内容本身。
                Logger.LogError(ex,
                    "{Service}: dropped one audit operation the database refused (function {FunctionName}, user {UserId}, url length {UrlLength}, user agent length {UserAgentLength}).",
                    ServiceName, operation.FunctionName, operation.UserId,
                    operation.Url?.Length ?? 0, operation.UserAgent?.Length ?? 0);
            }
        }

        if (dropped > 0)
        {
            Logger.LogWarning("{Service}: row-by-row retry finished; {Saved} of {Count} audit operations saved, {Dropped} dropped.",
                ServiceName, batch.Count - dropped, batch.Count, dropped);
        }
    }
}
