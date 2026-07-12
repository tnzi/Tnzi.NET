
namespace Tnzi.System;

[DependsOn(typeof(EFCoreModule))]
[OptionalDependsOn(typeof(SignalRModule))]
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

        // 注册应用程序配置选项并启用启动时验证（section 路径由 [ConfigSection] 派生）
        context.Services.AddTnziOptions<ApplicationOptions, ApplicationOptionsValidator>(configuration);

        // 注册配置加密选项（显式嵌套 section）
        context.Services.AddTnziOptions<SettingEncryptionOptions, SettingEncryptionOptionsValidator>(configuration, "System:Encryption");

        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // Code-declared permissions for this module's admin surfaces - the
        // Authorization module's PermissionDbSeeder picks every registered
        // provider up on startup (no-op when Authorization is not loaded).
        context.Services.AddTransient<IPermissionDefinitionProvider, SystemPermissions>();
        // Bridge: derive a grantable view/update code per settings group so the
        // config center becomes per-module role-assignable (not all-or-nothing).
        // Registered AFTER SystemPermissions so the "system" group keeps its
        // Technical default (first-wins); this provider only adds permissions.
        context.Services.AddTransient<IPermissionDefinitionProvider, SettingsPermissionDefinitionProvider>();

        var services = context.Services;

        // 注册菜单服务
        services.AddScoped<IMenuService, MenuService>();

        // 注册配置服务
        services.AddScoped<ISettingService, SettingService>();
        services.AddScoped<ISettingsCenterService, SettingsCenterService>();
        services.AddSingleton<ISettingDefinitionProvider, AttributeSettingDefinitionProvider>();

        // 注册全局外观（管理端主题）服务
        services.AddScoped<IAppearanceService, AppearanceService>();

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
        // 应用未调 builder.Configuration.AddTnziSettings() 时, source 为 null, IOptionsMonitor 自动 no-op。
        var settingSource = context.Configuration.GetTnziSettingsSource();
        if (settingSource != null)
        {
            services.AddSingleton(settingSource);
        }

        // 实时推送：SignalR 加载时注册 SettingsRealtimeHub 的按 Hub 推送服务，
        // 让 SettingChangedEventHandler 能向所有连接客户端广播配置变更（免手动刷新页面）。
        var signalRLoaded = services.FirstOrDefault(s => s.ServiceType == typeof(ITnziApplication))
                ?.ImplementationInstance is ITnziApplication tnziApp
            && tnziApp.IsModuleLoaded<SignalRModule>();
        if (signalRLoaded)
        {
            services.AddScoped<IMessagePushService<SettingsRealtimeHub>, MessagePushService<SettingsRealtimeHub>>();
        }

        // 注册配置变更处理器：只要 IConfiguration 热重载(source)或实时广播(SignalR) 任一可用即注册。
        // 两者都不可用时(既没 AddTnziSettings 也没 SignalR)本地链 no-op，无需注册。
        if (settingSource != null || signalRLoaded)
        {
            services.AddEventHandler<SettingChangedEvent, SettingChangedEventHandler>();
        }

        // 多实例一致性收端无条件注册（未订阅时零开销）：即使本实例没接 source/SignalR，
        // 跨实例清按键缓存（MemoryCache 每实例独立）依然必要。分布式总线是否加载在
        // OnApplicationInitializationAsync 才可知，订阅在 init 阶段按需接线。
        // 具体类型注册供分布式收端复用本地 reload+广播逻辑（可选依赖缺失时自动 no-op）。
        services.AddScoped<SettingChangedEventHandler>();
        services.AddEventHandler<SettingChangedIntegrationEvent, SettingChangedDistributedEventHandler>();

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
        else
        {
            // 沉默失败护栏：存在热设置定义却没接 AddTnziSettings 时，配置中心 UI 照常可编辑、
            // 保存也"成功"，但值永远流不进 IOptionsMonitor — 必须在启动期把话说明白。
            var hasRuntimeSettings = context.ServiceProvider
                .GetServices<ISettingDefinitionProvider>()
                .SelectMany(p => p.GetGroups())
                .Any();
            if (hasRuntimeSettings)
            {
                context.ServiceProvider.GetService<ILogger<SystemModule>>()?.LogWarning(
                    "Runtime setting groups are defined but the host did not call builder.Configuration.AddTnziSettings(). " +
                    "Settings-center writes will persist but will NOT flow into IConfiguration/IOptionsMonitor " +
                    "(admin changes take no effect until AddTnziSettings is wired before Build()).");
            }
        }

        // 多实例一致性：分布式总线可用时订阅其他实例广播的配置变更（reload + 本实例 SignalR 广播）。
        var distributedBus = context.ServiceProvider.GetService<IDistributedEventBus>();
        if (distributedBus != null)
        {
            distributedBus.Subscribe<SettingChangedIntegrationEvent, SettingChangedDistributedEventHandler>();
        }

        // 仅在 SignalRModule 加载时映射设置实时广播 Hub（[Authorize]，WS 走 access_token query）。
        var webApp = context.WebApp;
        var tnziApp = context.ServiceProvider.GetService<ITnziApplication>();
        if (webApp != null && tnziApp != null && tnziApp.IsModuleLoaded<SignalRModule>())
        {
            webApp.MapHub<SettingsRealtimeHub>("/hubs/settings");
        }
    }
}
