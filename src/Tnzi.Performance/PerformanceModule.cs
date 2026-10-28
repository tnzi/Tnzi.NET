
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

        // ★ 挂在 AspNetCore 管线的「认证之后、限流与授权之前」插入点，而不是在 OnApplicationInitializationAsync 里追加：
        // 追加只能落在授权之后，401 / 403 / 429 由授权与限流中间件就地写出、不再调用下游，样本里只剩通过了的请求。
        context.Services.AddRequestPipelineMiddleware<PerformanceMiddleware>(RequestPipelineStage.AfterAuthentication);

        return Task.CompletedTask;
    }
}
