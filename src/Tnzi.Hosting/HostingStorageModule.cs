
namespace Tnzi.Hosting;

/// <summary>
/// Hosting 存储模块
/// 包含：系统管理、文件存储、Swagger、本地化
/// 适用于仅需要文件存储功能的轻量项目
/// </summary>
[DependsOn(
    typeof(AspNetCoreModule),
    typeof(StorageModule),
    typeof(SystemModule),
    typeof(SwaggerModule),
    typeof(LocalizationModule)
)]
public abstract class HostingStorageModule : TnziApplicationModule
{
}
