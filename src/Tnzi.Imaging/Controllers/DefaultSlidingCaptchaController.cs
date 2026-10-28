namespace Tnzi.Imaging.Controllers;

/// <summary>
/// Default sliding captcha controller for puzzle-based verification
/// </summary>
[DefaultController]
[Route("captcha/sliding")]
[AllowAnonymous]
[ApiExplorerSettings(GroupName = "captcha")]
public class DefaultSlidingCaptchaController : ApiControllerBase
{
    protected readonly ISlidingCaptchaService SlidingCaptchaService;

    public DefaultSlidingCaptchaController(ISlidingCaptchaService slidingCaptchaService)
    {
        SlidingCaptchaService = Check.NotNull(slidingCaptchaService);
    }

    /// <summary>
    /// Generate a sliding captcha puzzle
    /// </summary>
    /// <remarks>
    /// ★ 与 <c>generate-adaptive</c> 出同一种题（按失败历史调难度）。这是 <c>sliding</c> 提供商下发给客户端的出题地址；
    /// 此前它出固定难度的题且不计失败，于是框架自己的接线里自适应难度从未生效，而客户端即便改用
    /// <c>generate-adaptive</c>，攻击脚本只要调这里就永远拿到最低难度与等价的通行令牌。
    /// 没有失败历史时出的题与配置完全一致。
    /// </remarks>
    /// <param name="purpose">Purpose the pass token issued on success is bound to (e.g. <c>login</c>). Optional for the puzzle itself, but a pass token issued without one is refused by every protected endpoint.</param>
    [HttpPost("generate")]
    public virtual async Task<ApiResult<SlidingCaptchaDto>> Generate([FromQuery] string? purpose = null)
    {
        if (purpose != null && !CaptchaPurpose.IsValid(purpose))
            return BadRequest<SlidingCaptchaDto>("Invalid captcha purpose.");

        var result = await SlidingCaptchaService.GenerateAdaptiveAsync(purpose);
        return result.ToApiResult();
    }

    /// <summary>
    /// Verify user's sliding captcha answer
    /// </summary>
    [HttpPost("verify")]
    public virtual async Task<ApiResult<SlidingCaptchaVerifyResult>> Verify([FromBody] SlidingCaptchaVerifyRequest request)
    {
        var result = await SlidingCaptchaService.VerifyAsync(request.Token, request.X);
        return result.ToApiResult();
    }

    /// <summary>
    /// Generate a sliding captcha with adaptive difficulty based on failure history
    /// </summary>
    /// <remarks>
    /// ★ 刻意<b>不接受</b>客户端标识：它此前是一个 <c>[FromQuery] clientId</c>，
    /// 而难度正是按它查失败次数的 —— 不传或每次换一个值就永远是最低难度，
    /// 填别人的值能把对方顶到最高。现在由服务端从当前请求派生。
    /// </remarks>
    /// <param name="purpose">Purpose the pass token issued on success is bound to (e.g. <c>login</c>). Optional for the puzzle itself, but a pass token issued without one is refused by every protected endpoint.</param>
    [HttpPost("generate-adaptive")]
    public virtual async Task<ApiResult<SlidingCaptchaDto>> GenerateAdaptive([FromQuery] string? purpose = null)
    {
        if (purpose != null && !CaptchaPurpose.IsValid(purpose))
            return BadRequest<SlidingCaptchaDto>("Invalid captcha purpose.");

        var result = await SlidingCaptchaService.GenerateAdaptiveAsync(purpose);
        return result.ToApiResult();
    }
}
