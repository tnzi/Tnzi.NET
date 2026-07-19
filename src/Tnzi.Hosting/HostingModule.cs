
namespace Tnzi.Hosting;

/// <summary>
/// Hosting 模块（智能版）
/// 基线依赖：AspNetCore + Swagger + Localization
/// 通过 [OptionalDependsOn] 自适应已加载的业务模块，自动激活模块提供的 [DefaultController]
/// 事件处理器仅在 Identity + Notification 同时加载时注册
/// </summary>
[DependsOn(
    typeof(AspNetCoreModule),
    typeof(SwaggerModule),
    typeof(LocalizationModule)
)]
[OptionalDependsOn(typeof(IdentityModule))]
[OptionalDependsOn(typeof(AuthorizationModule))]
[OptionalDependsOn(typeof(FeatureModule))]
[OptionalDependsOn(typeof(StorageModule))]
[OptionalDependsOn(typeof(TemplateModule))]
[OptionalDependsOn(typeof(NotificationModule))]
[OptionalDependsOn(typeof(AIModule))]
[OptionalDependsOn(typeof(AISkillsModule))]
[OptionalDependsOn(typeof(AIWorkflowModule))]
[OptionalDependsOn(typeof(AIMcpModule))]
[OptionalDependsOn(typeof(AuditModule))]
[OptionalDependsOn(typeof(SystemModule))]
[OptionalDependsOn(typeof(HangfireModule))]
[OptionalDependsOn(typeof(SignalRModule))]
[OptionalDependsOn(typeof(ChatModule))]
[OptionalDependsOn(typeof(PaymentModule))]
[OptionalDependsOn(typeof(FinanceModule))]
[OptionalDependsOn(typeof(FinanceAiModule))]
[OptionalDependsOn(typeof(FinanceDocumentsModule))]
[OptionalDependsOn(typeof(PayrollModule))]
[OptionalDependsOn(typeof(AIRagModule))]
[OptionalDependsOn(typeof(Tnzi.AI.Sandbox.AISandboxModule))]
[OptionalDependsOn(typeof(AIChannelsModule))]
public abstract class HostingModule : TnziApplicationModule
{
    // 注意：
    // - Controller 程序集注册：由 AspNetCoreModule 根据启动模块继承链自动注册
    // - [DefaultController] 激活：由本模块注册 DefaultControllerEnabledMarker 实现
    // - 模板路径配置：由 TemplateModule 自动扫描（如果加载了 Template 模块）

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 激活 [DefaultController] 标记的 Controller
        context.Services.AddSingleton<DefaultControllerEnabledMarker>();

        // 条件注册事件处理器（需要 Identity + Notification 同时加载）
        var appDescriptor = context.Services.FirstOrDefault(s => s.ServiceType == typeof(ITnziApplication));
        if (appDescriptor?.ImplementationInstance is ITnziApplication app
            && app.IsModuleLoaded<IdentityModule>()
            && app.IsModuleLoaded<NotificationModule>())
        {
            context.Services.AddEventHandler<UserRegisteredEvent, UserRegisteredEventHandler>();
            context.Services.AddEventHandler<PasswordResetRequestedEvent, PasswordResetRequestedEventHandler>();
            context.Services.AddEventHandler<TwoFactorCodeSentEvent, TwoFactorCodeSentEventHandler>();
        }

        return base.ConfigureServicesAsync(context);
    }
}
