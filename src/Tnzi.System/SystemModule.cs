
namespace Tnzi.System;

[DependsOn(typeof(EFCoreModule))]
public class SystemModule : TnziApplicationModule
{
    /// <summary>
    /// 表名前缀
    /// </summary>
    public override string? TableNamePrefix => "Sys";

    /// <summary>
    /// 加载顺序
    /// </summary>
    public override int LoadOrder => 5;

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var configuration = context.Configuration;

        // 注册应用程序配置选项并启用启动时验证
        context.Services.AddOptions<ApplicationOptions>()
            .Bind(configuration.GetSection("System"))
            .ValidateWith<ApplicationOptions, ApplicationOptionsValidator>();

        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var services = context.Services;

        // 注册菜单服务
        services.AddScoped<IMenuService, MenuService>();

        // 注册配置服务
        services.AddScoped<ISettingService, SettingService>();

        // 注册访问日志服务
        services.AddScoped<IAccessLogService, AccessLogService>();

        // 注册访问日志异步处理
        var accessLogSender = new AccessLogSender();
        services.AddSingleton<IAccessLogSender>(accessLogSender);
        services.AddSingleton<IAccessLogConsumer>(accessLogSender);
        services.AddHostedService<AccessLogBackgroundService>();

        return Task.CompletedTask;
    }
}
