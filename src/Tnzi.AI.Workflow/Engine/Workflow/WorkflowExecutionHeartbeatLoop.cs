namespace Tnzi.AI.Workflow.Engine;

/// <summary>
/// 在引擎执行期间驱动 <see cref="IWorkflowExecutionHeartbeat"/> 的后台循环；释放即停止。
/// </summary>
/// <remarks>
/// 一次心跳失败只记 Warning，循环继续：心跳是可观测性，不是让工作流失败的理由。
/// 停止时等待正在进行的那一次心跳结束，保证引擎返回后不再有心跳落库 —— 否则终态更新之后
/// 又一次"还活着"的时间戳会让行看起来仍在运行。
/// </remarks>
internal sealed class WorkflowExecutionHeartbeatLoop : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop;
    private readonly Task _loop;

    private WorkflowExecutionHeartbeatLoop(IWorkflowExecutionHeartbeat heartbeat, string executionId, ILogger logger, CancellationToken cancellationToken)
    {
        _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = RunAsync(heartbeat, executionId, logger, _stop.Token);
    }

    /// <summary>
    /// 有可用的心跳实现且执行实例有 Id（即存在要保活的 <c>WorkflowExecution</c> 行）时启动循环，否则返回 null。
    /// </summary>
    public static WorkflowExecutionHeartbeatLoop? Start(IServiceProvider serviceProvider, string? executionId, ILogger logger, CancellationToken cancellationToken)
    {
        Check.NotNull(serviceProvider);
        Check.NotNull(logger);

        if (string.IsNullOrWhiteSpace(executionId))
            return null;

        var heartbeat = serviceProvider.GetService<IWorkflowExecutionHeartbeat>();
        if (heartbeat == null || heartbeat.Interval <= TimeSpan.Zero)
            return null;

        return new WorkflowExecutionHeartbeatLoop(heartbeat, executionId, logger, cancellationToken);
    }

    private static async Task RunAsync(IWorkflowExecutionHeartbeat heartbeat, string executionId, ILogger logger, CancellationToken stop)
    {
        // 让出调用线程：引擎的第一层执行不该等第一次心跳。
        await Task.Yield();

        using var timer = new PeriodicTimer(heartbeat.Interval);
        while (true)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stop))
                    return;

                await heartbeat.BeatAsync(executionId, stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Heartbeat for workflow execution '{ExecutionId}' failed; the run continues, the watchdog may misjudge it if this persists.", executionId);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
            // 停止本身触发的取消
        }
        finally
        {
            _stop.Dispose();
        }
    }
}
