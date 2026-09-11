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
    [HttpPost("generate")]
    public virtual async Task<ApiResult<SlidingCaptchaDto>> Generate()
    {
        var result = await SlidingCaptchaService.GenerateAsync();
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
    [HttpPost("generate-adaptive")]
    public virtual async Task<ApiResult<SlidingCaptchaDto>> GenerateAdaptive()
    {
        var result = await SlidingCaptchaService.GenerateAdaptiveAsync();
        return result.ToApiResult();
    }
}
