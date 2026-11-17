using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Tnzi.AspNetCore.Options;

/// <summary>
/// 迁移后启动任务（<see cref="IPostMigrationStartupTask"/>）失败后的后台重试与健康报告。配置路径：<c>AspNetCore:StartupTasks</c>。
/// </summary>
/// <remarks>
/// <para>
/// 启动那一刻数据库不可达（宿主重启后出口 IP 还没进白名单、切换到另一站点）时，任务失败不打断启动。
/// 失败的任务在后台从 <see cref="RetryDelay"/> 起、每轮翻倍、封顶 <see cref="MaxRetryDelay"/> 重试，直到各自完成一次；
/// 在那之前健康检查 <c>startup-tasks</c> 报告它们：声明 <see cref="IPostMigrationStartupTask.RequiredForReadiness"/>
/// 的任务按 <see cref="RequiredTaskHealthStatus"/>（默认 Unhealthy，<c>/health/ready</c> 答 503），其余为 Degraded。
/// </para>
/// </remarks>
[ConfigSection("AspNetCore:StartupTasks")]
public class PostMigrationStartupTaskOptions
{
    /// <summary>首次失败后第一次重试前等多久（默认 2 秒），之后每轮翻倍。</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>重试间隔的上限（默认 1 分钟）：数据库恢复之后最多这么久，任务就补做完。</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 必须先完成才能接流量的任务（<see cref="IPostMigrationStartupTask.RequiredForReadiness"/>）还没完成时
    /// 健康检查 <c>startup-tasks</c> 报告的状态（默认 <see cref="HealthStatus.Unhealthy"/>）。
    /// 宁可让这段时间照常接流量的部署设 <c>Degraded</c>（答 200，正文里看得到）。
    /// </summary>
    public HealthStatus RequiredTaskHealthStatus { get; set; } = HealthStatus.Unhealthy;
}
