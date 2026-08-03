namespace Tnzi.Payment.Controllers;

/// <summary>
/// 支付方式（绑卡）控制器：用户保存可复用的支付方式，供订阅自动续费无人值守扣款。
/// </summary>
[ApiAuthorize]
[ApiExplorerSettings(GroupName = "user")]
[Route("payment-methods")]
[DefaultController]
public class DefaultPaymentMethodController : ApiControllerBase
{
    private readonly IPaymentMethodService _paymentMethodService;

    public DefaultPaymentMethodController(IPaymentMethodService paymentMethodService)
    {
        _paymentMethodService = Check.NotNull(paymentMethodService);
    }

    protected IPaymentMethodService PaymentMethodService => _paymentMethodService;

    /// <summary>
    /// 创建绑卡会话。返回二者之一：<c>ClientSecret</c>（前端调渠道 SDK 就地收集，含 3DS），
    /// 或 <c>ApprovalUrl</c>（把用户整页送去渠道授权，如 PayPal）。完成后调 <c>POST /payment-methods</c> 登记。
    /// </summary>
    [HttpPost("setup")]
    public virtual async Task<ApiResult<SetupSessionDto>> CreateSetupSession([FromBody] CreateSetupSessionDto request)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _paymentMethodService.CreateSetupSessionAsync(userId, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 登记支付方式：绑卡会话完成后用渠道返回的 token 调用，落库供后续自动扣款使用
    /// </summary>
    [HttpPost]
    public virtual async Task<ApiResult<StoredPaymentMethodDto>> Bind([FromBody] BindPaymentMethodDto request)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _paymentMethodService.BindAsync(userId, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取我的支付方式列表
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<List<StoredPaymentMethodDto>>> GetList()
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _paymentMethodService.GetUserMethodsAsync(userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 设为默认支付方式
    /// </summary>
    [HttpPost("{id:guid}/default")]
    public virtual async Task<ApiResult> SetDefault(Guid id)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _paymentMethodService.SetDefaultAsync(userId, id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除（解绑）支付方式
    /// </summary>
    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> Remove(Guid id)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _paymentMethodService.RemoveAsync(userId, id);
        return result.ToApiResult();
    }
}
