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
    [HttpPost("generate-adaptive")]
    public virtual async Task<ApiResult<SlidingCaptchaDto>> GenerateAdaptive([FromQuery] string? clientId = null)
    {
        var result = await SlidingCaptchaService.GenerateAdaptiveAsync(clientId);
        return result.ToApiResult();
    }
}
