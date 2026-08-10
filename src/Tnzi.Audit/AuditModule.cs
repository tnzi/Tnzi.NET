namespace Tnzi.Audit;

/// <summary>
/// 审计模块
/// </summary>
[DependsOn(typeof(EFCoreModule), typeof(AspNetCoreModule))]
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

        // 记录级读取审计选项（可选能力，Enabled 默认 false）。
        // 必须在 PreConfigure 阶段注册：实体配置在模型构建期读它来决定是否建表。
        context.Services.AddTnziOptions<RecordAccessAuditOptions>(context.Configuration);

        // 策略驱动数据销毁选项（可选能力，Enabled 默认 false）。同上，条件建表要在模型构建期读它。
        context.Services
            .AddTnziOptions<DataDestructionOptions, DataDestructionOptionsValidator>(context.Configuration);

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

        // 实体级审计采集管道：EF SaveChanges 拦截器 + per-request collector。
        // 拦截器注册为 IInterceptor 服务，经 Tnzi.EFCore 的 AddTnziDbContext seam
        // 自动挂进所有 Tnzi DbContext——仅 Audit 模块加载时生效（未加载则 seam
        // 解析不到任何 IInterceptor，零开销）。两者均 Scoped：DbContext options
        // 按 scope 构建，拦截器与 AuditMiddleware（从 RequestServices 解析）
        // 拿到的是同一个请求级 collector 实例。
        context.Services.AddScoped<IEntityAuditCollector, EntityAuditCollector>();
        context.Services.AddScoped<IInterceptor, EntityAuditSaveChangesInterceptor>();

        // 注册操作审计服务
        context.Services.AddScoped<IAuditOperationService, AuditOperationService>();

        // 记录级读取审计（可选能力）。服务始终注册，未启用时其方法是空操作——
        // 这样业务代码可以无条件注入并调用，不必自己判断开关。
        context.Services.AddScoped<IRecordAccessAuditor, RecordAccessAuditor>();

        // 策略驱动数据销毁（可选能力）。销毁动作用 TryAdd 注册默认实现：消费应用在自己的模块里
        // Add 一个 IDataDestroyer（如匿名化）即可胜出——同一服务的多条注册，DI 解析取最后一条。
        // 后台服务始终注册，未启用时它立刻退出，不占任何资源。
        context.Services.TryAddScoped<IDataDestroyer, HardDeleteDataDestroyer>();
        context.Services.AddScoped<IDataDestructionService, DataDestructionService>();
        context.Services.AddHostedService<DataDestructionBackgroundService>();

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
