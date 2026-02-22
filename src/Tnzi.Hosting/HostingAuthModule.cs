
namespace Tnzi.Hosting;

/// <summary>
/// Hosting 认证授权模块
/// 包含：认证、授权、系统管理、Swagger、本地化
/// 适用于需要认证授权但不需要存储/通知等功能的项目
/// </summary>
[DependsOn(
    typeof(AspNetCoreModule),
    typeof(IdentityModule),
    typeof(AuthorizationModule),
    typeof(SystemModule),
    typeof(SwaggerModule),
    typeof(LocalizationModule)
)]
public abstract class HostingAuthModule : TnziApplicationModule
{
}
