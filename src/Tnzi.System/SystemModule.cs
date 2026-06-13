
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

        // 注册配置加密选项
        context.Services.AddOptions<SettingEncryptionOptions>()
            .Bind(configuration.GetSection("System:Encryption"))
            .ValidateWith<SettingEncryptionOptions, SettingEncryptionOptionsValidator>();

        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var services = context.Services;

        // 注册菜单服务
        services.AddScoped<IMenuService, MenuService>();

        // 注册配置服务
        services.AddScoped<ISettingService, SettingService>();
        services.AddScoped<ISettingsCenterService, SettingsCenterService>();
        services.AddSingleton<ISettingDefinitionProvider, SystemSettingDefinitionProvider>();

        // 注册配置加密器（仅在启用加密时注册）
        var encryptionOptions = context.Configuration
            .GetSection("System:Encryption")
            .Get<SettingEncryptionOptions>();
        if (encryptionOptions is { Enabled: true })
        {
            services.AddSingleton<ISettingEncryptor, AesSettingEncryptor>();
        }

        // 注册配置提供者（分层解析链：User → Tenant → Global）
        services.AddScoped<ISettingProvider, GlobalSettingProvider>();
        services.AddScoped<ISettingProvider, TenantSettingProvider>();
        services.AddScoped<ISettingProvider, UserSettingProvider>();

        // 注册访问日志服务
        services.AddScoped<IAccessLogService, AccessLogService>();

        // 注册访问日志异步处理
        var accessLogSender = new AccessLogSender();
        services.AddSingleton<IAccessLogSender>(accessLogSender);
        services.AddSingleton<IAccessLogConsumer>(accessLogSender);
        services.AddHostedService<AccessLogBackgroundService>();

        // SettingConfigurationSource: 把 host builder 阶段注册的 source 暴露成 DI singleton，
        // 让 OnApplicationInitializationAsync 拿同一实例 attach IServiceProvider + 触发首次 reload。
        // 应用未调 builder.Configuration.AddTnziSettings() 时, source 为 null, handler/IOptionsMonitor 自动 no-op。
        var settingSource = context.Configuration.GetTnziSettingsSource();
        if (settingSource != null)
        {
            services.AddSingleton(settingSource);
            services.AddEventHandler<SettingChangedEvent, SettingChangedEventHandler>();
        }

        return Task.CompletedTask;
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        // SettingConfigurationSource 在 host build 阶段创建时未持有 IServiceProvider；
        // 此刻 DI 已 ready，attach root provider 让 source 首次从 DB pull 配置 + IOptionsMonitor 拿到 DB 值。
        var source = context.ServiceProvider.GetService<SettingConfigurationSource>();
        if (source != null)
        {
            await source.AttachAsync(context.ServiceProvider);
        }
    }
}
