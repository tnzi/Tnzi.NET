namespace Tnzi.Identity.Mvc;

/// <summary>
/// 标记一个端点需要二次确认（step-up）：会话有效还不够，必须刚证明过是本人。
/// </summary>
/// <remarks>
/// <para>
/// 用法：<c>[RequireStepUp("tip.attachment.download")]</c>。范围名由应用自定，
/// 前端拿到 <c>STEP_UP_REQUIRED</c> 后拉起确认交互（passkey 或验证码），
/// 完成后原样重试这次请求。
/// </para>
/// <para>
/// <b>与 <c>[ApiAuthorize]</c> 正交</b>：那个回答「你有没有这个权限」，本特性回答
/// 「此刻按下按钮的是不是你」。两个都要过，顺序上先过授权 —— 让一个本来就没权限的人
/// 先去做一次生物识别，既没意义又泄露了「这个端点存在」。
/// </para>
/// <para>
/// ★ <b>未启用 <c>Identity:StepUp</c> 时本特性不拦任何请求。</b>它是加固项而不是运行前提，
/// 漏配的后果应当是「没有额外保护」，而不是「这些功能全都用不了」。
/// 要让它成为硬性要求，把开关列进上线校验。
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class RequireStepUpAttribute : TypeFilterAttribute
{
    /// <summary>
    /// 初始化一个 <see cref="RequireStepUpAttribute"/> 类型的新实例。
    /// </summary>
    /// <param name="scope">
    /// 确认范围。一次确认只覆盖同名范围的端点 ——
    /// 为下载附件做的确认，不该顺便把「删除全部记录」也放行。
    /// </param>
    public RequireStepUpAttribute(string scope)
        : base(typeof(StepUpFilter))
    {
        Check.NotNullOrWhiteSpace(scope);

        Scope = scope;
        Arguments = [scope];
    }

    /// <summary>确认范围。</summary>
    public string Scope { get; }
}

/// <summary>
/// <see cref="RequireStepUpAttribute"/> 的执行体。
/// </summary>
public class StepUpFilter : IAsyncActionFilter
{
    private readonly string _scope;
    private readonly IStepUpService _stepUpService;
    private readonly IOptionsMonitor<IdentityOptions> _options;

    /// <summary>
    /// 初始化一个 <see cref="StepUpFilter"/> 类型的新实例。
    /// </summary>
    public StepUpFilter(string scope, IStepUpService stepUpService, IOptionsMonitor<IdentityOptions> options)
    {
        _scope = Check.NotNullOrWhiteSpace(scope);
        _stepUpService = Check.NotNull(stepUpService);
        _options = Check.NotNull(options);
    }

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        Check.NotNull(context);
        Check.NotNull(next);

        if (await _stepUpService.IsSatisfiedAsync(_scope, context.HttpContext.RequestAborted))
        {
            await next();
            return;
        }

        var statusCode = _options.CurrentValue.StepUp.ChallengeStatusCode;

        // 走标准信封，前端的统一错误处理才认得它。errors 里带上范围，
        // 前端才知道该为哪一个动作发起确认；错误码固定，据此区分
        // 「要再证明一次」与「会话过期了」—— 两者都可能是 401。
        context.Result = new ObjectResult(
            ApiResult.Error(
                "This action requires re-authentication",
                statusCode,
                ErrorCodes.IDENTITY_STEP_UP_REQUIRED,
                new { scope = _scope }))
        {
            StatusCode = statusCode
        };
    }
}
