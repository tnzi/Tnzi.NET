namespace Tnzi.Audit;

/// <summary>
/// 审计模块
/// </summary>
[DependsOn(typeof(AspNetCoreModule))]
public class AuditModule : TnziApplicationModule
{
    /// <summary>
    /// 审计模块加载顺序
    /// </summary>
    public override int LoadOrder => 20;

    /// <summary>
    /// 表名前缀
    /// </summary>
    public override string? TableNamePrefix => "Audit";

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddTnziOptions<AuditOptions, AuditOptionsValidator>(context.Configuration);

        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // Code-declared permissions for this module's admin surfaces - the
        // Authorization module's PermissionDbSeeder picks every registered
        // provider up on startup (no-op when Authorization is not loaded).
        context.Services.AddTransient<IPermissionDefinitionProvider, AuditPermissions>();

        // 注册请求体脱敏工具（无状态，可单例）
        context.Services.AddSingleton<RequestBodyRedactor>();

        // 注册审计存储
        context.Services.AddScoped<IAuditStore, DatabaseAuditStore>();

        // 注册操作审计服务
        context.Services.AddScoped<IAuditOperationService, AuditOperationService>();

        // 注册异步审计发送者与消费者（单例）
        context.Services.AddSingleton<AuditSender>();
        context.Services.AddSingleton<IAuditSender>(sp => sp.GetRequiredService<AuditSender>());
        context.Services.AddSingleton<IAuditConsumer>(sp => sp.GetRequiredService<AuditSender>());

        // 注册审计后台服务
        context.Services.AddHostedService<AuditBackgroundService>();

        return Task.CompletedTask;
    }

    public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        var app = context.App;
        if (app != null)
        {
            // 审计应该尽早执行，但要在异常处理之后
            // AspNetCoreModule 注册了 ExceptionHandlingMiddleware
            // 这里我们希望 AuditMiddleware 记录所有请求结果
            app.UseMiddleware<AuditMiddleware>();
        }

        return Task.CompletedTask;
    }
}
