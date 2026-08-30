namespace Tnzi.Identity.Controllers;

/// <summary>
/// <see cref="DefaultAuthController"/> 的二次确认（step-up）端点。
/// </summary>
/// <remarks>
/// <para>
/// 前端拿到 <c>IDENTITY_STEP_UP_REQUIRED</c> 后调这里完成确认，再原样重试原来那次请求。
/// 确认过程不产生任何客户端持有的凭据，结果记在服务端。
/// </para>
/// <para>
/// ★ <b>没有 begin 端点。</b>passkey 的挑战沿用已有的
/// <c>passkey/assert/begin</c> —— 二次确认与登录用的是同一套断言，
/// 再造一条并行的挑战链路只会多一处要各自维护的状态。
/// </para>
/// </remarks>
public partial class DefaultAuthController
{
    /// <summary>
    /// 用 passkey 完成一次二次确认。
    /// </summary>
    /// <remarks>
    /// 必须已登录：这个能力是加在会话之上的。断言若属于另一个账号一律拒绝，
    /// 与验证码错误共用同一句回答。
    /// </remarks>
    [HttpPost("step-up/passkey")]
    [Authorize]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual async Task<ApiResult<StepUpGrantDto>> StepUpWithPasskey([FromBody] StepUpPasskeyDto input)
    {
        if (StepUpService == null)
        {
            return StepUpUnavailable<StepUpGrantDto>();
        }

        Check.NotNull(input);

        var result = await StepUpService.VerifyWithPasskeyAsync(input, input.Scope);
        return result.ToApiResult();
    }

    /// <summary>
    /// 发送一枚二次确认专用的验证码到当前用户已验证的邮箱 / 手机号。
    /// </summary>
    /// <remarks>
    /// ★ 这枚码<b>只能</b>用于 <c>step-up/code</c>：它的用途是
    /// <c>VerificationCodePurpose.StepUp</c>，拿去登录 / 换绑 / 重置密码一律无效，
    /// 反过来那些流程发出的码也完成不了二次确认。
    /// <para>
    /// 地址不作为入参：要证明的是「当前这个账号的主人在场」，
    /// 由调用方指定收件地址等于把这件事交给了可能已经被接管的会话。
    /// </para>
    /// </remarks>
    [HttpPost("step-up/send-code")]
    [Authorize]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual async Task<ApiResult<string?>> SendStepUpCode([FromBody] SendStepUpCodeDto input)
    {
        if (StepUpService == null)
        {
            return StepUpUnavailable<string?>();
        }

        Check.NotNull(input);

        var result = await StepUpService.SendCodeAsync(input.Type);
        return result.ToApiResult();
    }

    /// <summary>
    /// 用二次验证码完成一次二次确认。
    /// </summary>
    /// <remarks>
    /// 强度低于 passkey（短信与邮件码都可被中继）。对高风险动作应当只开 passkey 一条路，
    /// 办法是不要把这个端点暴露给那些流程，而不是在这里做判断 ——
    /// 「哪个动作允许哪种确认」是应用的决定。
    /// </remarks>
    [HttpPost("step-up/code")]
    [Authorize]
    [ApiExplorerSettings(GroupName = "auth")]
    public virtual async Task<ApiResult<StepUpGrantDto>> StepUpWithCode([FromBody] StepUpCodeDto input)
    {
        if (StepUpService == null)
        {
            return StepUpUnavailable<StepUpGrantDto>();
        }

        Check.NotNull(input);

        var result = await StepUpService.VerifyWithCodeAsync(input.Code, input.Type, input.Scope);
        return result.ToApiResult();
    }

    /// <summary>
    /// 未注册二次确认服务时的统一回答。
    /// </summary>
    private static ApiResult<T> StepUpUnavailable<T>()
        => Result<T>.Failure("Step-up verification is not available", 400, ErrorCodes.CONFIGURATION_ERROR).ToApiResult();
}
