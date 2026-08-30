namespace Tnzi.Payment.Services;

/// <summary>
/// 支付模块后台任务服务。
/// 定期执行本模块自己的两条扫描（过期支付关闭、在途退款对账），
/// 再跑一遍各可选域经 <see cref="IPaymentScheduledScan"/> 贡献进来的扫描。
/// </summary>
/// <remarks>
/// ★ <b>解析必须在 try 之内</b>：拆分前三个服务是在一轮开始时一次性
/// <c>GetRequiredService</c> 出来的，三个解析都在 <c>try</c> 之外、却被最外层那个
/// 「记一条 Error 就等下一轮」的 <c>catch (Exception)</c> 罩着 —— 于是只要**任何一个**
/// 服务解析不出来，这一轮的**全部**扫描都不执行，包括与它毫不相干的两条。
/// 症状是「后台任务安静地什么都不做」，日志里只有一句看不出因果的解析异常。
/// 这是一个独立于拆包就该修的缺陷；拆包只是让它从「理论上」变成「必然」。
/// </remarks>
public class PaymentBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PaymentBackgroundService> _logger;
    private readonly IOptionsMonitor<PaymentOptions> _options;

    public PaymentBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<PaymentBackgroundService> logger,
        IOptionsMonitor<PaymentOptions> options)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        _logger = Check.NotNull(logger);
        _options = Check.NotNull(options);
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
                await ExecuteTasksAsync(stoppingToken);
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

    private async Task ExecuteTasksAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var services = scope.ServiceProvider;

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
