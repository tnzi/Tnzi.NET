
namespace Tnzi.Modules;

/// <summary>
/// Tnzi 核心服务模块
/// 注册核心服务（性能监控、常用的工具服务等）
/// </summary>
public class CoreServicesModule : TnziCoreModule
{
    public override int LoadOrder => 0;

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 注册性能监控服务
        context.Services.TryAddSingleton<IPerformanceMonitorService, PerformanceMonitorService>();

        return Task.CompletedTask;
    }
}