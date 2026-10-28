namespace Tnzi.Payment.Controllers;

/// <summary>
/// 当前用户所代表的计费账户（账单资料）。查看要 <c>payment.account.view</c>、修改要 <c>payment.account.manage</c>
///：只在 Installation / Tenant 范围下检查，User 范围下每人恒可管理自己的账户（服务层判）。
/// </summary>
[ApiAuthorize]
[ApiExplorerSettings(GroupName = "user")]
[Route("billing-account")]
[DefaultController]
public class DefaultBillingAccountController : ApiControllerBase
{
    private readonly IBillingAccountService _billingAccountService;

    public DefaultBillingAccountController(IBillingAccountService billingAccountService)
    {
        _billingAccountService = Check.NotNull(billingAccountService);
    }

    /// <summary>
    /// 当前计费账户与资料
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<BillingAccountDto>> Get()
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _billingAccountService.GetCurrentAsync(userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新当前计费账户的资料
    /// </summary>
    [HttpPut]
    public virtual async Task<ApiResult<BillingAccountDto>> Update([FromBody] UpdateBillingAccountDto request)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await _billingAccountService.UpdateCurrentAsync(userId, request);
        return result.ToApiResult();
    }
}
