namespace Tnzi.Finance.Controllers.Admin;

/// <summary>
/// 汇率管理控制器
/// </summary>
[Route("admin/finance/exchange-rates")]
[DefaultController]
[ApiAuthorize(PermissionName = "finance.rate.view")]
public class DefaultFinanceExchangeRateAdminController : ApiAdminControllerBase
{
    private readonly IExchangeRateService _exchangeRateService;

    public DefaultFinanceExchangeRateAdminController(IExchangeRateService exchangeRateService)
    {
        _exchangeRateService = Check.NotNull(exchangeRateService);
    }

    protected IExchangeRateService ExchangeRateService => _exchangeRateService;

    /// <summary>
    /// 分页查询汇率
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<ExchangeRateDto>>> GetList([FromQuery] ExchangeRateQueryDto query)
    {
        var result = await _exchangeRateService.GetListAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 录入/更新汇率（按 币种对 + 日期 幂等）
    /// </summary>
    [HttpPost]
    public virtual async Task<ApiResult<ExchangeRateDto>> Upsert([FromBody] UpsertExchangeRateDto request)
    {
        var result = await _exchangeRateService.UpsertAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除汇率
    /// </summary>
    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await _exchangeRateService.DeleteAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 从外部提供者刷新汇率
    /// </summary>
    [HttpPost("refresh")]
    public virtual async Task<ApiResult<int>> Refresh()
    {
        var result = await _exchangeRateService.RefreshFromProviderAsync();
        return result.ToApiResult();
    }
}
