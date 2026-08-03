namespace Tnzi.Payment.Services;

/// <summary>
/// 支付模块后台任务服务
/// 定期执行：过期支付关闭、订阅到期续费
/// </summary>
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

        var paymentService = scope.ServiceProvider.GetRequiredService<IPaymentService>();
        var refundService = scope.ServiceProvider.GetRequiredService<IRefundService>();
        var subscriptionService = scope.ServiceProvider.GetRequiredService<ISubscriptionService>();

        // 每个扫描独立隔离：任一环节失败不影响其余扫描本轮执行
        await RunScanAsync("close expired payments",
            () => paymentService.CloseExpiredPaymentsAsync(cancellationToken));

        // 对账在途退款：把渠道侧已终结但本地仍是"退款中"的记录推进到终态
        await RunScanAsync("reconcile pending refunds",
            () => refundService.ReconcilePendingRefundsAsync(cancellationToken));

        // 续费到期订阅（off-session 扣款）
        await RunScanAsync("renew due subscriptions",
            () => subscriptionService.RenewExpiredSubscriptionsAsync(cancellationToken));

        // 试用到期转正/过期
        await RunScanAsync("convert due trials",
            () => subscriptionService.ConvertDueTrialsAsync(cancellationToken));

        // 暂停到期自动恢复
        await RunScanAsync("resume due paused subscriptions",
            () => subscriptionService.ResumeDuePausedSubscriptionsAsync(cancellationToken));

        // 到期未续费 / 逾期超宽限期 → 过期
        await RunScanAsync("expire overdue subscriptions",
            () => subscriptionService.ExpireOverdueSubscriptionsAsync(cancellationToken));

        // 续费提醒：在扣款前 N 天通知用户，尤其是尚未绑卡的
        await RunScanAsync("send renewal reminders",
            () => subscriptionService.SendRenewalRemindersAsync(cancellationToken));
    }

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
