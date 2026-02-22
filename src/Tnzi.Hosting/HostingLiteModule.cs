
namespace Tnzi.Hosting;

/// <summary>
/// Hosting Lite 模块
/// 包含：系统管理（无需认证）
/// 提供开箱即用的 Controller 实现和预制模板
/// </summary>
[DependsOn(
    typeof(AspNetCoreModule),
    typeof(SystemModule),
    typeof(SwaggerModule),
    typeof(LocalizationModule)
)]
public abstract class HostingLiteModule : TnziApplicationModule
{
    // 注意：
    // - Controller 程序集注册：由 TnziApplication 自动处理
    // - 模板路径配置：由 TemplateModule 自动扫描（如果加载了 Template 模块）

    // 如需自定义配置，可重写以下方法：
    // - PreConfigureServicesAsync(ServiceConfigurationContext context)
    // - ConfigureServicesAsync(ServiceConfigurationContext context)
    // - PostConfigureServicesAsync(ServiceConfigurationContext context)
}