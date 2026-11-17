using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Tnzi.System.Options;

/// <summary>
/// 数据库设置进 <c>IConfiguration</c> 的热链（<c>SettingConfigurationSource</c>）的选项。配置路径：<c>System:Settings</c>。
/// </summary>
/// <remarks>
/// <para>
/// 启动时从 <c>Sys_Setting</c> 读 Global 设置，读不到（数据库那一刻不可达）不打断启动：应用先用 appsettings 的值跑，
/// 后台按 <see cref="InitialLoadRetryDelay"/> 起、每次翻倍、封顶 <see cref="InitialLoadMaxRetryDelay"/> 重试，直到读到为止；
/// 读到即替换、触发 reload token，<c>IOptionsMonitor</c> 的订阅者照常收到变更通知。
/// 在那之前健康检查 <c>settings</c> 报告 <see cref="NotLoadedHealthStatus"/>，所以 <c>/health/ready</c> 不就绪。
/// </para>
/// <para>只管「从未读到过」这一段。读到过一次之后，变更重载失败保持上一次的值、交给事件总线重试，与此无关。</para>
/// </remarks>
[ConfigSection("System:Settings")]
public class SettingConfigurationOptions
{
    /// <summary>
    /// 是否自动注册热链（默认 <c>true</c>）。在服务注册之前直接从 <c>IConfiguration</c> 读，
    /// 所以只有 appsettings / 环境变量里的值算数，写在 <c>Sys_Setting</c> 里不起作用。
    /// </summary>
    public bool EnableConfigurationSource { get; set; } = true;

    /// <summary>首次读取失败后第一次重试前等多久（默认 2 秒），之后每次翻倍。</summary>
    public TimeSpan InitialLoadRetryDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>重试间隔的上限（默认 1 分钟）：数据库恢复之后最多这么久，真实的值就到位。</summary>
    public TimeSpan InitialLoadMaxRetryDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 设置还没从数据库读到时健康检查 <c>settings</c> 报告的状态（默认 <see cref="HealthStatus.Unhealthy"/>：
    /// <c>/health</c> 与 <c>/health/ready</c> 答 503）。宁可让这段时间照常接流量的部署设 <c>Degraded</c>（答 200，正文里看得到）。
    /// </summary>
    public HealthStatus NotLoadedHealthStatus { get; set; } = HealthStatus.Unhealthy;
}
