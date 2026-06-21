namespace Tnzi.Imaging;

/// <summary>
/// 图片处理与验证码模块
/// </summary>
[DependsOn(
    typeof(TnziCoreModule)
)]
public class ImagingModule : TnziInfrastructureModule
{
    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddTnziOptions<ImagingOptions, ImagingOptionsValidator>(context.Configuration);

        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 注册 ValidateCoder (使用 Options 配置)
        context.Services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ImagingOptions>>().Value;
            var captcha = options.Captcha;

            return new ValidateCoder
            {
                FontSize = captcha.FontSize,
                FontWidth = captcha.FontWidth > 0 ? captcha.FontWidth : captcha.FontSize,
                Height = captcha.Height,
                HasBorder = captcha.HasBorder,
                RandomPosition = captcha.RandomPosition,
                RandomColor = captcha.RandomColor,
                RandomItalic = captcha.RandomItalic,
                RandomPointPercent = captcha.RandomPointPercent,
                RandomLineCount = captcha.RandomLineCount
            };
        });

        // 注册验证码服务
        context.Services.TryAddScoped<IVerifyCodeService, VerifyCodeService>();

        // 注册滑动验证码服务
        context.Services.TryAddScoped<ISlidingCaptchaService, SlidingCaptchaService>();

        return Task.CompletedTask;
    }
}
