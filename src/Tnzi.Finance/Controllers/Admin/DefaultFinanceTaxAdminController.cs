namespace Tnzi.Finance.Controllers.Admin;

/// <summary>
/// 税模型管理控制器（机构/税率/税码）
/// </summary>
[Route("admin/finance/taxes")]
[DefaultController]
[ApiAuthorize(PermissionName = "finance.tax.view")]
public class DefaultFinanceTaxAdminController : ApiAdminControllerBase
{
    private readonly ITaxService _taxService;

    public DefaultFinanceTaxAdminController(ITaxService taxService)
    {
        _taxService = Check.NotNull(taxService);
    }

    protected ITaxService TaxService => _taxService;

    // ── 税务机构 ──────────────────────────────────────────────

    /// <summary>
    /// 获取全部税务机构
    /// </summary>
    [HttpGet("agencies")]
    public virtual async Task<ApiResult<List<TaxAgencyDto>>> GetAgencies()
    {
        var result = await _taxService.GetAgenciesAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建税务机构
    /// </summary>
    [HttpPost("agencies")]
    [ApiAuthorize(PermissionName = "finance.tax.create")]
    public virtual async Task<ApiResult<TaxAgencyDto>> CreateAgency([FromBody] UpsertTaxAgencyDto request)
    {
        var result = await _taxService.CreateAgencyAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新税务机构
    /// </summary>
    [HttpPut("agencies/{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.tax.update")]
    public virtual async Task<ApiResult<TaxAgencyDto>> UpdateAgency(Guid id, [FromBody] UpsertTaxAgencyDto request)
    {
        var result = await _taxService.UpdateAgencyAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除税务机构
    /// </summary>
    [HttpDelete("agencies/{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.tax.delete")]
    public virtual async Task<ApiResult> DeleteAgency(Guid id)
    {
        var result = await _taxService.DeleteAgencyAsync(id);
        return result.ToApiResult();
    }

    // ── 税率 ─────────────────────────────────────────────────

    /// <summary>
    /// 获取税率列表（可按机构过滤）
    /// </summary>
    [HttpGet("rates")]
    public virtual async Task<ApiResult<List<TaxRateDto>>> GetRates([FromQuery] Guid? agencyId = null)
    {
        var result = await _taxService.GetRatesAsync(agencyId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建税率
    /// </summary>
    [HttpPost("rates")]
    [ApiAuthorize(PermissionName = "finance.tax.create")]
    public virtual async Task<ApiResult<TaxRateDto>> CreateRate([FromBody] UpsertTaxRateDto request)
    {
        var result = await _taxService.CreateRateAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新税率
    /// </summary>
    [HttpPut("rates/{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.tax.update")]
    public virtual async Task<ApiResult<TaxRateDto>> UpdateRate(Guid id, [FromBody] UpsertTaxRateDto request)
    {
        var result = await _taxService.UpdateRateAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除税率
    /// </summary>
    [HttpDelete("rates/{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.tax.delete")]
    public virtual async Task<ApiResult> DeleteRate(Guid id)
    {
        var result = await _taxService.DeleteRateAsync(id);
        return result.ToApiResult();
    }

    // ── 税码 ─────────────────────────────────────────────────

    /// <summary>
    /// 获取全部税码（含组件）
    /// </summary>
    [HttpGet("codes")]
    public virtual async Task<ApiResult<List<TaxCodeDto>>> GetCodes()
    {
        var result = await _taxService.GetCodesAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建税码
    /// </summary>
    [HttpPost("codes")]
    [ApiAuthorize(PermissionName = "finance.tax.create")]
    public virtual async Task<ApiResult<TaxCodeDto>> CreateCode([FromBody] UpsertTaxCodeDto request)
    {
        var result = await _taxService.CreateCodeAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新税码（组件全量替换）
    /// </summary>
    [HttpPut("codes/{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.tax.update")]
    public virtual async Task<ApiResult<TaxCodeDto>> UpdateCode(Guid id, [FromBody] UpsertTaxCodeDto request)
    {
        var result = await _taxService.UpdateCodeAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除税码
    /// </summary>
    [HttpDelete("codes/{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.tax.delete")]
    public virtual async Task<ApiResult> DeleteCode(Guid id)
    {
        var result = await _taxService.DeleteCodeAsync(id);
        return result.ToApiResult();
    }
}
