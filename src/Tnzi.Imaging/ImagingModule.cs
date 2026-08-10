namespace Tnzi.Imaging;

/// <summary>
/// 图片处理与验证码模块
/// </summary>
/// <remarks>
/// 依赖 <see cref="AspNetCoreModule"/> 而非核心：本模块自带
/// <c>DefaultSlidingCaptchaController : ApiControllerBase</c>，控制器基类与
/// <c>[DefaultController]</c> 激活机制都来自 ASP.NET Core 集成层。
/// <para>
/// ★ 这里曾写着 <c>[DependsOn(typeof(TnziCoreModule))]</c>，而
/// <c>TnziCoreModule</c> 是<b>模块类型层级的抽象基类</b>，不是可加载的模块 ——
/// <c>ModuleLoader</c> 会试图实例化它并抛「must have a parameterless constructor」，
/// 即任何显式加载本模块的应用<b>启动即失败</b>。它长期没暴露，是因为唯一的消费者
/// （Identity / Storage）用的是 <c>[OptionalDependsOn]</c>，而可选依赖只在模块<b>已被</b>
/// 加载时连接、从不主动加载它，于是本模块从未真的进过模块图。
/// </para>
/// </remarks>
[DependsOn(typeof(AspNetCoreModule))]
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
