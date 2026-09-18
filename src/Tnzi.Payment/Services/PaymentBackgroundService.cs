namespace Tnzi.Payment.Services;

/// <summary>
/// 支付模块后台任务服务。
/// 定期执行本模块自己的两条扫描（过期支付关闭、在途退款对账），
/// 再跑一遍各可选域经 <see cref="IPaymentScheduledScan"/> 贡献进来的扫描。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>解析必须在 try 之内</b>：拆分前三个服务是在一轮开始时一次性
/// <c>GetRequiredService</c> 出来的，三个解析都在 <c>try</c> 之外、却被最外层那个
/// 「记一条 Error 就等下一轮」的 <c>catch (Exception)</c> 罩着 —— 于是只要**任何一个**
/// 服务解析不出来，这一轮的**全部**扫描都不执行，包括与它毫不相干的两条。
/// 症状是「后台任务安静地什么都不做」，日志里只有一句看不出因果的解析异常。
/// 这是一个独立于拆包就该修的缺陷；拆包只是让它从「理论上」变成「必然」。
/// </para>
/// <para>
/// ★ <b>多租户开启时按租户逐个跑。</b>后台 scope 没有请求上下文，当前租户为空，全局过滤器成了
/// <c>TenantId IS NULL</c>：八条扫描每一轮都 <c>Processed 0</c>，零 Warning —— 过期单不关、券不还、
/// 续费不发生、逾期不过期。每一条扫描的推进都是带过滤器的条件更新，租户不对就安静地影响 0 行，
/// 所以不能靠扫描自己 <c>IgnoreQueryFilters</c>，必须整轮切进那个租户。要扫哪些租户由
/// <see cref="IPaymentTenantSource"/> 贡献（并集，宁可多扫不可漏扫）；未开启多租户时一遍跑完，行为不变。
/// </para>
/// </remarks>
public class PaymentBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PaymentBackgroundService> _logger;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly bool _multiTenancyEnabled;

    public PaymentBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<PaymentBackgroundService> logger,
        IOptionsMonitor<PaymentOptions> options,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        _logger = Check.NotNull(logger);
        _options = Check.NotNull(options);
        _multiTenancyEnabled = multiTenancyOptions?.Value.Enabled ?? false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PaymentBackgroundService started. Interval: {Interval}min", _options.CurrentValue.BackgroundTaskIntervalMinutes);

        // 启动后延迟 30 秒再开始首次执行，避免应用启动时负载
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PaymentBackgroundService encountered an error");
            }

            // 每轮迭代读取 CurrentValue，使 admin 配置中心对执行间隔的改动即时生效
            var interval = TimeSpan.FromMinutes(_options.CurrentValue.BackgroundTaskIntervalMinutes);
            await Task.Delay(interval, stoppingToken);
        }

        _logger.LogInformation("PaymentBackgroundService stopped");
    }

    /// <summary>
    /// 跑一轮全部扫描（多租户开启时对每个租户各跑一遍）。公开是为了能被手工触发与被测试直接驱动，
    /// 不必等 <see cref="ExecuteAsync"/> 的启动延迟与轮询间隔。
    /// </summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!_multiTenancyEnabled)
        {
            using var scope = _serviceProvider.CreateScope();
            await RunAllScansAsync(scope.ServiceProvider, cancellationToken);
            return;
        }

        foreach (var tenantId in await CollectTenantIdsAsync(cancellationToken))
        {
            // 每个租户一个 scope：一个 DbContext 的变更跟踪里不混两个租户的实体
            using var scope = _serviceProvider.CreateScope();
            var currentTenant = scope.ServiceProvider.GetService<ICurrentTenant>();
            using (currentTenant?.Change(tenantId))
            {
                await RunAllScansAsync(scope.ServiceProvider, cancellationToken);
            }
        }
    }

    /// <summary>
    /// 汇总各来源报出的租户（并集）。来源本身解析不出来或查询失败都只记 Error，不掀掉整轮：
    /// 剩下的来源照常贡献，最差是这一轮少扫一批租户，下一轮再来。
    /// </summary>
    private async Task<IReadOnlyList<Guid?>> CollectTenantIdsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var tenantIds = new HashSet<Guid?>();

        foreach (var source in scope.ServiceProvider.GetServices<IPaymentTenantSource>())
        {
            try
            {
                foreach (var tenantId in await source.GetTenantIdsAsync(cancellationToken))
                    tenantIds.Add(tenantId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Payment tenant source {Source} failed; its tenants are skipped this round", source.GetType().Name);
            }
        }

        if (tenantIds.Count == 0)
            _logger.LogDebug("Multi-tenancy is enabled and no tenant has pending payment work this round");

        return tenantIds.ToList();
    }

    private async Task RunAllScansAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        // 每个扫描独立隔离：任一环节失败（含**服务解析失败**）不影响其余扫描本轮执行。
        await RunScanAsync("close expired payments",
            () => services.GetRequiredService<IPaymentService>().CloseExpiredPaymentsAsync(cancellationToken));

        // 对账在途退款：把渠道侧已终结但本地仍是"退款中"的记录推进到终态
        await RunScanAsync("reconcile pending refunds",
            () => services.GetRequiredService<IRefundService>().ReconcilePendingRefundsAsync(cancellationToken));

        // 可选域贡献的扫描（续费到期、试用转正、暂停恢复、逾期过期、续费提醒等，
        // 全部随 Tnzi.Payment.Subscriptions 而来）。没有实现就是空集合，上面两条照跑不误。
        // GetServices 本身放在 try 之外是安全的：IEnumerable<T> 永远解析得到，最差是空集合。
        foreach (var scan in services.GetServices<IPaymentScheduledScan>())
            await RunScanAsync(scan.Name, () => scan.RunAsync(cancellationToken));
    }

    /// <summary>
    /// 跑一条扫描，把它的失败关在自己这一条里。
    /// </summary>
    /// <remarks>
    /// <paramref name="scan"/> 是 <c>Func</c> 而不是已经求过值的 <c>Task</c>：
    /// 服务解析写在委托里，因此解析异常也落在这里的 <c>catch</c> 中，而不是掀掉整轮。
    /// </remarks>
    private async Task RunScanAsync(string scanName, Func<Task<Result<int>>> scan)
    {
        try
        {
            var result = await scan();
            if (result.Succeeded && result.Data > 0)
                _logger.LogInformation("Background scan '{Scan}' processed {Count} items", scanName, result.Data);
            else if (!result.Succeeded)
                _logger.LogWarning("Background scan '{Scan}' returned failure: {Error}", scanName, result.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background scan '{Scan}' failed", scanName);
        }
    }
}
