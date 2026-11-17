namespace Tnzi.AspNetCore.Startup;

/// <summary>
/// 启动那一轮里失败的迁移后启动任务，在后台退避重试直到各自完成一次。
/// </summary>
/// <remarks>
/// 等启动那一轮跑完（宿主从不跑它时就一直等到停机），然后只要还有没完成的任务：
/// 等 <see cref="PostMigrationStartupTaskOptions.RetryDelay"/>，重跑没完成的那些，仍有失败就把间隔翻倍、
/// 封顶 <see cref="PostMigrationStartupTaskOptions.MaxRetryDelay"/>。全部完成即结束。
/// </remarks>
internal sealed class PostMigrationStartupTaskRetryService(
    PostMigrationStartupTaskRunner runner,
    IServiceProvider serviceProvider,
    IOptionsMonitor<PostMigrationStartupTaskOptions> options,
    ILogger<PostMigrationStartupTaskRetryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await runner.InitialRunCompleted.WaitAsync(stoppingToken);

            var delay = options.CurrentValue.RetryDelay;
            while (runner.HasIncomplete)
            {
                await Task.Delay(delay, stoppingToken);
                if (await runner.RetryIncompleteAsync(serviceProvider, stoppingToken))
                {
                    logger.LogInformation("All post-migration startup tasks have completed.");
                    return;
                }

                delay = Min(delay * 2, options.CurrentValue.MaxRetryDelay);
                var incomplete = runner.States?.Where(s => !s.Completed).Select(s => s.Name) ?? [];
                logger.LogWarning(
                    "Post-migration startup task(s) not completed yet: {Tasks}. Next try in {Delay}.",
                    string.Join(", ", incomplete), delay);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 宿主停止。
        }
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
}
