using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Tnzi.AspNetCore.Startup;

/// <summary>
/// 健康检查 <c>startup-tasks</c>：迁移后启动任务是否都完成过一次。
/// </summary>
/// <remarks>
/// 失败过、还在后台重试的任务里有 <see cref="IPostMigrationStartupTask.RequiredForReadiness"/> 的，报告
/// <see cref="PostMigrationStartupTaskOptions.RequiredTaskHealthStatus"/>（默认 Unhealthy，<c>/health/ready</c> 答 503）；
/// 只有其余任务时报告 Degraded（答 200，正文里点名）。全部完成、或宿主没有跑这一步时为 Healthy。
/// </remarks>
internal sealed class PostMigrationStartupTaskHealthCheck(
    PostMigrationStartupTaskRunner runner,
    IOptionsMonitor<PostMigrationStartupTaskOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var states = runner.States;
        if (states == null)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Post-migration startup tasks have not run on this host."));
        }

        var failing = states.Where(s => s.Failing).ToList();
        if (failing.Count == 0)
        {
            return Task.FromResult(HealthCheckResult.Healthy($"{states.Count} post-migration startup task(s) completed."));
        }

        var status = HealthStatus.Healthy;
        if (failing.Any(s => s.Task.RequiredForReadiness))
        {
            status = Worst(status, options.CurrentValue.RequiredTaskHealthStatus);
        }

        if (failing.Any(s => !s.Task.RequiredForReadiness))
        {
            status = Worst(status, HealthStatus.Degraded);
        }

        var data = failing.ToDictionary(
            s => s.Name,
            s => (object)$"{s.FailedAttempts} failed attempt(s) since {s.FailingSince:O}; required for readiness: {s.Task.RequiredForReadiness}");

        return Task.FromResult(new HealthCheckResult(
            status,
            $"Post-migration startup task(s) not completed since startup, retried in the background: {string.Join(", ", failing.Select(s => s.Name))}.",
            failing[0].LastFailure,
            data));
    }

    /// <summary>HealthStatus 越小越差（Unhealthy = 0）。</summary>
    private static HealthStatus Worst(HealthStatus a, HealthStatus b) => a < b ? a : b;
}
