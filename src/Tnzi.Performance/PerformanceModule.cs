
namespace Tnzi.Performance;

/// <summary>
/// 性能分析模块
/// 提供性能指标收集、慢请求检测等功能
/// 配置路径：Performance
/// </summary>
[DependsOn(typeof(AspNetCoreModule))]
public class PerformanceModule : TnziInfrastructureModule
{
    /// <summary>
    /// 在 AspNetCore 模块之后加载
    /// </summary>
    public override int LoadOrder => 15;

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 注册配置选项并启用启动时验证
        context.Services.AddTnziOptions<PerformanceOptions, PerformanceOptionsValidator>(context.Configuration);

        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // Code-declared permissions for this module's admin surfaces - the
        // Authorization module's PermissionDbSeeder picks every registered
        // provider up on startup (no-op when Authorization is not loaded).
        context.Services.AddTransient<IPermissionDefinitionProvider, PerformancePermissions>();

        var config = context.Configuration.GetSection("Performance").Get<PerformanceOptions>()
            ?? new PerformanceOptions();

        if (!config.Enabled)
        {
            // 关闭时中间件不会注册，因此不会有采样进来；这里仍注册一个容量 1 的
            // 收集器（等效空操作），避免下游注入 IPerformanceCollector 时 DI 解析失败
            context.Services.AddSingleton<IPerformanceCollector>(_ => new PerformanceCollector(1));
            return Task.CompletedTask;
        }

        // 注册性能收集器
        context.Services.AddSingleton<IPerformanceCollector>(_ => new PerformanceCollector(config.MaxHistorySize));

        return Task.CompletedTask;
    }

    public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        var app = context.App;
        if (app == null)
        {
            return Task.CompletedTask;
        }

        var config = context.ServiceProvider
            .GetRequiredService<IOptions<PerformanceOptions>>()
            .Value;

        // 如果未启用，不注册中间件
        if (!config.Enabled)
        {
            return Task.CompletedTask;
        }

        // 注册性能分析中间件（应该在请求追踪中间件之后）
        app.UseMiddleware<PerformanceMiddleware>();

        return Task.CompletedTask;
    }
}
