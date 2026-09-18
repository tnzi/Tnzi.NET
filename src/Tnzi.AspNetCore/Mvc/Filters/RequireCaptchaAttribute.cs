namespace Tnzi.AspNetCore.Mvc.Filters;

/// <summary>
/// 给一个匿名可达的写端点挂上人机验证：没有有效令牌的请求拿到 400 <c>CAPTCHA_REQUIRED</c>，action 不执行。
/// </summary>
/// <remarks>
/// <para>
/// 令牌来源依次为请求头 <c>X-Captcha-Token</c>、实现了 <see cref="ICaptchaProtectedRequest"/> 的 action 参数。
/// 用途（<see cref="Purpose"/>）用来把令牌绑在端点上：出评分的提供商回传 action、Altcha 把它写进 salt、
/// 图形与滑块按它分缓存键 —— 在注册页解出的令牌拿到登录页一律拒绝。
/// </para>
/// <para>
/// <b>按需启用</b>：没配 <c>AspNetCore:Captcha:Provider</c> 时本特性放行，只在启动期记一条 Warning
/// 点名它挂在哪些端点上。框架不能因为消费方没配提供商就让它的路由 500，但也不能让「挂了特性」
/// 被误读成「有保护」，所以那条 Warning 是必需的。
/// </para>
/// <para>
/// 这只是 HTTP 边界上的闸门。Identity 的登录 / 注册流程<b>不</b>用它：那里的判定在服务层
/// （自适应阈值、失败计数、拒绝时随响应附一道新题），特性给不了这些。消费方自己的联系表单、
/// 匿名投稿、评论这类端点才是它的用途。
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireCaptchaAttribute : Attribute, IFilterFactory, IOrderedFilter
{
    /// <summary>携带令牌的请求头名。</summary>
    public const string TokenHeaderName = "X-Captcha-Token";

    /// <summary>
    /// 初始化特性。
    /// </summary>
    /// <param name="purpose">端点用途，例如 <c>contact</c> / <c>comment</c>；小写字母、数字与连字符。</param>
    public RequireCaptchaAttribute(string purpose)
    {
        Purpose = Check.NotNullOrWhiteSpace(purpose);
    }

    /// <summary>端点用途。</summary>
    public string Purpose { get; }

    /// <summary>
    /// 排在框架的全局 <see cref="ModelStateValidationFilter"/>（Order 0）之后、业务 action 之前：
    /// 令牌是一次性的，一个模型验证就会拒掉的请求不该先把它烧掉。
    /// </summary>
    public int Order { get; set; } = 100;

    /// <inheritdoc />
    public bool IsReusable => false;

    /// <inheritdoc />
    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
    {
        Check.NotNull(serviceProvider);
        return new RequireCaptchaFilter(
            Purpose,
            serviceProvider.GetRequiredService<ICaptchaVerifier>(),
            serviceProvider.GetRequiredService<ILogger<RequireCaptchaFilter>>());
    }
}
