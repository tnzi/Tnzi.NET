
using Tnzi.Imaging.Services;

namespace Tnzi.Imaging;

/// <summary>
/// 图片处理与验证码模块
/// </summary>
[DependsOn(
    typeof(TnziCoreModule)
)]
public class ImagingModule : TnziInfrastructureModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 注册ValidateCoder
        context.Services.AddSingleton<ValidateCoder>();

        // 注册验证码服务
        context.Services.TryAddScoped<IVerifyCodeService, VerifyCodeService>();

        return Task.CompletedTask;
    }
}
