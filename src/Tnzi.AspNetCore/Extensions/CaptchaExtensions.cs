namespace Tnzi.AspNetCore.Extensions;

/// <summary>
/// 人机验证提供商的注册入口。
/// </summary>
public static class CaptchaExtensions
{
    /// <summary>
    /// 注册一个 <see cref="ICaptchaProvider"/> 实现。多个实现并存，由 <c>AspNetCore:Captcha:Provider</c> 挑选；
    /// 与框架内置同名的实现后注册者生效（消费方可借此覆盖内置的某一家）。
    /// </summary>
    public static IServiceCollection AddCaptchaProvider<TProvider>(this IServiceCollection services)
        where TProvider : class, ICaptchaProvider
    {
        Check.NotNull(services);
        services.AddScoped<ICaptchaProvider, TProvider>();
        return services;
    }

    /// <summary>
    /// 用工厂注册一个 <see cref="ICaptchaProvider"/>（同一个类型带不同参数注册多次时用，
    /// 例如四条 siteverify 描述符共用 <see cref="SiteVerifyCaptchaProvider"/>）。
    /// </summary>
    public static IServiceCollection AddCaptchaProvider(this IServiceCollection services, Func<IServiceProvider, ICaptchaProvider> factory)
    {
        Check.NotNull(services);
        Check.NotNull(factory);
        services.AddScoped(factory);
        return services;
    }

    /// <summary>
    /// 注册一条 siteverify 协议的提供商描述符。框架已内置 reCAPTCHA v2 / v3、hCaptcha、Turnstile；
    /// 协议同形的别家（例如 mCaptcha 的 siteverify 兼容端点）经此一行接入。
    /// </summary>
    public static IServiceCollection AddSiteVerifyCaptchaProvider(this IServiceCollection services, SiteVerifyCaptchaDescriptor descriptor)
    {
        Check.NotNull(services);
        Check.NotNull(descriptor);
        return services.AddCaptchaProvider(sp => new SiteVerifyCaptchaProvider(
            descriptor,
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<IOptionsSnapshot<CaptchaVerifierOptions>>(),
            sp.GetRequiredService<ILogger<SiteVerifyCaptchaProvider>>()));
    }
}
