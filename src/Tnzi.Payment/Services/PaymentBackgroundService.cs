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

        // 关闭过期支付
        try
        {
            var paymentService = scope.ServiceProvider.GetRequiredService<IPaymentService>();
            var closedResult = await paymentService.CloseExpiredPaymentsAsync(cancellationToken);
            if (closedResult.Succeeded && closedResult.Data > 0)
                _logger.LogInformation("Closed {Count} expired payments", closedResult.Data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to close expired payments");
        }

        var subscriptionService = scope.ServiceProvider.GetRequiredService<ISubscriptionService>();

        // 续费到期订阅（off-session 扣款）
        try
        {
            var renewedResult = await subscriptionService.RenewExpiredSubscriptionsAsync(cancellationToken);
            if (renewedResult.Succeeded && renewedResult.Data > 0)
                _logger.LogInformation("Processed renewal for {Count} due subscriptions", renewedResult.Data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to renew expired subscriptions");
        }

        // 试用到期转正/过期
        try
        {
            var trialResult = await subscriptionService.ConvertDueTrialsAsync(cancellationToken);
            if (trialResult.Succeeded && trialResult.Data > 0)
                _logger.LogInformation("Processed trial conversion for {Count} subscriptions", trialResult.Data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process due trials");
        }

        // 到期未续费 / 逾期超宽限期 → 过期
        try
        {
            var expiredResult = await subscriptionService.ExpireOverdueSubscriptionsAsync(cancellationToken);
            if (expiredResult.Succeeded && expiredResult.Data > 0)
                _logger.LogInformation("Expired {Count} overdue subscriptions", expiredResult.Data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to expire overdue subscriptions");
        }
    }
}
