namespace Tnzi.Finance.Recurring.Services;

/// <summary>
/// 到期扫描的后台循环
/// </summary>
/// <remarks>
/// 用 <see cref="BackgroundService"/> 而不是 <c>IBackgroundJobManager</c>：后者要加载
/// Hangfire，而"每月给客户开发票"不该以装一个作业调度器为前提。需要集中调度的
/// 部署把 <c>SweepIntervalMinutes</c> 设为 0 关掉本循环，改由自己的调度器打
/// <c>POST admin/finance/recurring/run-due</c> —— 两条路径走的是同一个方法。
///
/// **多实例安全**靠生成记录的唯一索引，不靠"只起一个实例"的约定：两个实例同时
/// 扫到同一期，第二个的插入会撞索引而不是给客户重开一张发票。
/// </remarks>
public class RecurringGenerationBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptionsMonitor<RecurringOptions> _options;
    private readonly ILogger<RecurringGenerationBackgroundService> _logger;

    public RecurringGenerationBackgroundService(
        IServiceProvider serviceProvider,
        IOptionsMonitor<RecurringOptions> options,
        ILogger<RecurringGenerationBackgroundService> logger)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minutes = _options.CurrentValue.SweepIntervalMinutes;
        if (minutes <= 0)
        {
            _logger.LogInformation(
                "Recurring document generation is not scheduled in-process (SweepIntervalMinutes <= 0); "
                + "drive it with POST admin/finance/recurring/run-due instead.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(5, minutes)));

        // 首个 tick 之后才跑，不与启动流程争抢连接。
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // 一次扫描失败不该终止循环 —— 记下来，下个周期重试。
                _logger.LogError(ex, "Recurring document sweep failed; will retry next interval.");
            }
        }
    }

    /// <remarks>
    /// ★**每个租户一个 DI 作用域**。作用域里装着 DbContext 与它的变更跟踪器；一个作用域
    /// 跑遍所有租户，只切 <see cref="ICurrentTenant"/>，会让租户 A 的实体留在跟踪器里被
    /// 租户 B 的 SaveChanges 一并刷库，而按主键取实体命中标识映射时**全局查询过滤器根本
    /// 不参与** —— 拿到的是上一个租户的行。内存也会随模板总数线性长上去。
    /// </remarks>
    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> tenantIds;

        using (var probe = _serviceProvider.CreateScope())
        {
            var tenantSource = probe.ServiceProvider.GetService<IRecurringTenantSource>();
            if (tenantSource == null)
            {
                // 未注册租户来源 -> 只扫环境上下文。缺省方向必须是"少生成"：
                // 给不该收到账单的租户凭空开出发票，比漏跑一期严重得多。
                await RunOnceAsync(probe.ServiceProvider, cancellationToken);
                return;
            }

            tenantIds = await tenantSource.GetTenantIdsAsync(cancellationToken);
        }

        foreach (var tenantId in tenantIds)
        {
            using var scope = _serviceProvider.CreateScope();
            var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            using (currentTenant.Change(tenantId))
            {
                await RunOnceAsync(scope.ServiceProvider, cancellationToken);
            }
        }
    }

    private async Task RunOnceAsync(IServiceProvider sp, CancellationToken cancellationToken)
    {
        var generator = sp.GetRequiredService<IRecurringGeneratorService>();
        var result = await generator.RunDueAsync(cancellationToken: cancellationToken);

        if (!result.Succeeded)
        {
            _logger.LogWarning("Recurring document sweep reported: {Message}", result.Message);
            return;
        }

        var data = result.Data!;
        if (data.Generated > 0 || data.Failed > 0 || data.Skipped > 0)
        {
            _logger.LogInformation(
                "Recurring sweep: {Templates} template(s) due, {Generated} generated, {Skipped} skipped, {Failed} failed.",
                data.TemplatesDue, data.Generated, data.Skipped, data.Failed);
        }
    }
}
