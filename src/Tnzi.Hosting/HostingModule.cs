
namespace Tnzi.Hosting;

/// <summary>
/// Hosting 模块（标准版）
/// 包含：认证、授权、云存储、通知、系统管理
/// 提供开箱即用的 Controller 实现和预制模板
/// </summary>
[DependsOn(
    typeof(AspNetCoreModule),
    typeof(IdentityModule),
    typeof(AuthorizationModule),
    typeof(StorageModule),
    typeof(TemplateModule),
    typeof(NotificationModule),
    typeof(SystemModule),
    typeof(SwaggerModule),
    typeof(LocalizationModule)
)]
public abstract class HostingModule : TnziApplicationModule
{
    // 注意：
    // - Controller 程序集注册：由 TnziApplication 自动处理
    // - 模板路径配置：由 TemplateModule 自动扫描（如果加载了 Template 模块）

    // 如需自定义配置，可重写以下方法：
    // - PreConfigureServicesAsync(ServiceConfigurationContext context)
    // - ConfigureServicesAsync(ServiceConfigurationContext context)
    // - PostConfigureServicesAsync(ServiceConfigurationContext context)

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 注册事件处理器（使用简化方式）
        context.Services.AddEventHandler<UserRegisteredEvent, UserRegisteredEventHandler>();
        context.Services.AddEventHandler<PasswordResetRequestedEvent, PasswordResetRequestedEventHandler>();
        context.Services.AddEventHandler<TwoFactorCodeSentEvent, TwoFactorCodeSentEventHandler>();

        return base.ConfigureServicesAsync(context);
    }
}

//