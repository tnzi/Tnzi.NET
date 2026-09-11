namespace Tnzi.AI.Workflow.Infrastructure;

/// <summary>
/// 驱动 <see cref="WorkflowWatchdogService"/> 的后台循环。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>它存在的理由是此前没有任何调度者</b>：<c>ScanAsync</c> 在全仓零调用方，模块只把
/// <see cref="WorkflowWatchdogService"/> 注册成 Scoped 服务。文档把"由宿主接调度"写成一个
/// 取舍，却既没有接线示例也没有启动提示 —— 而 <c>WorkflowWatchdogOptions.Enabled</c> 默认
/// true，看上去它一直在工作。实际结果是崩溃中断的执行实例永远停在 <c>Running</c>，
/// <c>AwaitingApproval</c> 永不过期。
/// </para>
/// <para>
/// 不依赖 Hangfire：工作流模块不该为了一个定时器多一条可选依赖。要换成 Hangfire 或别的
/// 外部调度器，把 <c>UseBuiltInScheduler</c> 关掉再自己调 <c>ScanAsync</c>；
/// <b>不要</b>关 <c>Enabled</c>，那是"根本不做超时检测"。
/// </para>
/// <para>
/// 多实例部署下每个实例都会扫描。这是安全的：标记超时是幂等的按行更新，两个实例同时
/// 标记同一行得到同一个结果。
/// </para>
/// </remarks>
public class WorkflowWatchdogHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<WorkflowWatchdogOptions> _options;
    private readonly ILogger<WorkflowWatchdogHostedService> _logger;

    public WorkflowWatchdogHostedService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<WorkflowWatchdogOptions> options,
        ILogger<WorkflowWatchdogHostedService> logger)
    {
        _scopeFactory = Check.NotNull(scopeFactory);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var current = _options.CurrentValue;

        if (!current.UseBuiltInScheduler)
        {
            // 说清楚"谁来调度"，否则关掉它与忘了接线在日志里长得一样。
            _logger.LogInformation(
                "Workflow watchdog built-in scheduler is off (AI:WorkflowWatchdog:UseBuiltInScheduler=false). " +
                "Stale executions will only be timed out if the host calls WorkflowWatchdogService.ScanAsync itself.");
            return;
        }

        if (!current.Enabled)
        {
            _logger.LogInformation(
                "Workflow watchdog is disabled (AI:WorkflowWatchdog:Enabled=false); executions stuck in Running " +
                "or awaiting approval will never be timed out.");
        }
        else
        {
            _logger.LogInformation(
                "Workflow watchdog scheduler started: scanning every {Interval}, running timeout {RunningTimeout}, " +
                "waiting timeout {WaitingTimeout}, max {MaxBatchSize} per scan.",
                current.ScanInterval, current.RunningTimeout, current.WaitingTimeout, current.MaxBatchSize);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.CurrentValue.ScanInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RunScanAsync(stoppingToken);
        }
    }

    /// <summary>
    /// 跑一次扫描。<c>Enabled</c> 由 <c>ScanAsync</c> 自己判定（每次读取），所以运行期
    /// 打开开关无需重启进程。
    /// </summary>
    private async Task RunScanAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var watchdog = scope.ServiceProvider.GetRequiredService<WorkflowWatchdogService>();
            await watchdog.ScanAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 关停，正常路径
        }
        catch (Exception ex)
        {
            // 一次扫描失败绝不能让循环退出：退出后不会有任何症状，只是超时检测再也不发生。
            _logger.LogError(ex, "Workflow watchdog scan failed; the scheduler keeps running.");
        }
    }
}
