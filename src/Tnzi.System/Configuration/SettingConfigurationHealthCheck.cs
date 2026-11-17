using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Tnzi.System.Configuration;

/// <summary>
/// 健康检查 <c>settings</c>：数据库里的设置是否至少读到过一次。
/// </summary>
/// <remarks>
/// 首次读取失败、后台重试还没成功时，应用在用 appsettings 的值跑（公司名、Logo 之类都是出厂占位），
/// 报告 <see cref="SettingConfigurationOptions.NotLoadedHealthStatus"/>（默认 Unhealthy），让 <c>/health/ready</c>
/// 与监控看得见。读到过一次之后恒为 Healthy：变更重载的暂时失败保持上一次的真实值，不算未就绪。
/// </remarks>
internal sealed class SettingConfigurationHealthCheck(
    SettingConfigurationSource source,
    IOptionsMonitor<SettingConfigurationOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var provider = source.Provider;
        var result = source.LoadState switch
        {
            SettingConfigurationLoadState.Loaded => HealthCheckResult.Healthy("Settings are loaded from the database."),
            SettingConfigurationLoadState.Unavailable => HealthCheckResult.Healthy("This host has no Setting repository; settings come from appsettings only."),
            SettingConfigurationLoadState.NotAttached => HealthCheckResult.Healthy("The settings source is not attached yet."),
            SettingConfigurationLoadState.Pending => new HealthCheckResult(
                options.CurrentValue.NotLoadedHealthStatus,
                "Settings are being loaded from the database."),
            _ => new HealthCheckResult(
                options.CurrentValue.NotLoadedHealthStatus,
                "Settings have not been loaded from the database since startup; the app is running on the appsettings values while a background retry continues.",
                provider?.LastFailure,
                new Dictionary<string, object>
                {
                    ["failedAttempts"] = provider?.FailedAttempts ?? 0,
                    ["failingSince"] = provider?.FailingSince?.ToString("O") ?? string.Empty,
                }),
        };
        return Task.FromResult(result);
    }
}
