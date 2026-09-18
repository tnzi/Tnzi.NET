namespace Tnzi.AspNetCore.Controllers;

/// <summary>
/// 人机验证的公开端点：告诉浏览器要渲染哪家控件，以及给自托管的 Altcha 出题。
/// </summary>
/// <remarks>
/// <b>匿名是设计</b>：登录页在任何凭据之前就要知道渲染什么，而这里下发的全是公开值
/// （站点密钥本来就写在页面里；Altcha 的题谁都可以领，解出来也只换得一次提交）。
/// 图形验证码与滑块各有自己的出题端点（<c>auth/captcha/{purpose}</c>、<c>captcha/sliding/*</c>）。
/// </remarks>
[Route("captcha")]
[DefaultController]
[AllowAnonymous]
[ApiExplorerSettings(GroupName = "captcha")]
public class DefaultCaptchaController : ApiControllerBase
{
    /// <summary>人机验证入口。</summary>
    protected readonly ICaptchaVerifier Verifier;

    /// <summary>已注册的提供商（用于找 Altcha 出题）。</summary>
    protected readonly IEnumerable<ICaptchaProvider> Providers;

    /// <summary>
    /// 初始化控制器。
    /// </summary>
    public DefaultCaptchaController(ICaptchaVerifier verifier, IEnumerable<ICaptchaProvider> providers)
    {
        Verifier = Check.NotNull(verifier);
        Providers = Check.NotNull(providers);
    }

    /// <summary>
    /// Get the captcha client configuration (provider, site key, script URL, challenge URL).
    /// </summary>
    [HttpGet("config")]
    public virtual ApiResult<CaptchaClientConfigDto> GetConfig()
        => Ok(Verifier.GetClientConfig());

    /// <summary>
    /// Issue an Altcha proof-of-work challenge bound to the given purpose.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>成功时回裸的 Altcha 挑战文档，不包 <c>ApiResult</c> 信封</b>：
    /// <c>{ algorithm, challenge, maxnumber, salt, signature }</c>。这个地址的消费方是 <c>&lt;altcha-widget&gt;</c> 控件本身，
    /// 它自己 <c>fetch</c> 出题地址并按 Altcha 协议校验响应体（有没有 <c>challenge</c> 键）；包了信封的
    /// <c>{ code, success, data }</c> 会被它判成「Challenge validation failed」，用户从验证码被要求那一刻起永远登不进去
    /// （2026-09-17 实发，此前的端到端测试自己拆信封，所以全绿）。与 <c>GET /auth/captcha/{purpose}</c> 回 PNG 同一条判据：
    /// 给第三方客户端消费的端点按那个客户端的协议回，框架的信封只给框架的 HttpClient。
    /// </para>
    /// <para>
    /// 用 <see cref="JsonResult"/> 而不是 <c>Ok(object)</c>：宿主开了 <c>AutoWrapApiResult</c> 时包装过滤器只包
    /// <c>ObjectResult</c>，<see cref="JsonResult"/> 不在其列，裸文档因此在任何宿主配置下都保持裸的。
    /// 失败（不是生效的提供商 404 / 用途非法 400 / 提供商没注册 500）仍回带真实状态码的信封：
    /// 控件对非 2xx 一律按失败处理、不读 body，而框架客户端读得到错误码。
    /// </para>
    /// </remarks>
    /// <param name="purpose">The purpose the solved challenge will be submitted for (e.g. <c>login</c>, <c>contact</c>).</param>
    [HttpGet("altcha/challenge")]
    [ProducesResponseType(typeof(AltchaChallengeDto), StatusCodes.Status200OK)]
    public virtual IActionResult GetAltchaChallenge([FromQuery] string purpose = "default")
    {
        if (!string.Equals(Verifier.ProviderName, AltchaCaptchaProvider.ProviderName, StringComparison.OrdinalIgnoreCase))
            return Envelope(NotFound<AltchaChallengeDto>("Altcha is not the active captcha provider."));

        if (!CaptchaPurpose.IsValid(purpose))
            return Envelope(BadRequest<AltchaChallengeDto>("Invalid captcha purpose."));

        var altcha = Providers.OfType<AltchaCaptchaProvider>().LastOrDefault();
        if (altcha == null)
            return Envelope(Error<AltchaChallengeDto>("Altcha provider is not registered.", 500));

        return new JsonResult(altcha.CreateChallenge(purpose));
    }

    /// <summary>失败信封按它自己的 <c>Code</c> 出 HTTP 状态码（与 action 直接返回 <c>ApiResult</c> 时的转换相同）。</summary>
    private static ObjectResult Envelope<T>(ApiResult<T> result) => new(result) { StatusCode = result.Code };
}
