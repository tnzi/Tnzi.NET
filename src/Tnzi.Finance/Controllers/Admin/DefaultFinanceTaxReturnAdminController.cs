namespace Tnzi.Finance.Controllers.Admin;

/// <summary>
/// 税务申报表
/// </summary>
/// <remarks>
/// 映射器按国家装（<c>IEnumerable</c> 注入，按 <c>CountryCode</c>+<c>FormCode</c> 查找）。
/// 一个部署可以同时装多个国家包；一个都没装时返回 501 引导，税码/税率/税务汇总
/// 报表照常可用。
/// </remarks>
[Route("admin/finance/tax-returns")]
[DefaultController]
[ApiAuthorize(PermissionName = "finance.report.view")]
public class DefaultFinanceTaxReturnAdminController : ApiAdminControllerBase
{
    private readonly IEnumerable<ITaxReturnMapper> _mappers;

    public DefaultFinanceTaxReturnAdminController(IEnumerable<ITaxReturnMapper> mappers)
    {
        _mappers = Check.NotNull(mappers);
    }

    /// <summary>
    /// 装了哪些申报表
    /// </summary>
    [HttpGet("forms")]
    public virtual ApiResult<List<TaxReturnFormDto>> GetForms()
    {
        var forms = _mappers
            .Select(m => new TaxReturnFormDto { Country = m.CountryCode, FormCode = m.FormCode })
            .OrderBy(f => f.Country).ThenBy(f => f.FormCode)
            .ToList();
        return Result<List<TaxReturnFormDto>>.Success(forms).ToApiResult();
    }

    /// <summary>
    /// 生成一张申报表
    /// </summary>
    [HttpGet("{country}/{formCode}")]
    public virtual async Task<ApiResult<TaxReturnDto>> Get(string country, string formCode, [FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        var mapper = _mappers.FirstOrDefault(m =>
            string.Equals(m.CountryCode, country, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(m.FormCode, formCode, StringComparison.OrdinalIgnoreCase));

        if (mapper == null)
        {
            var guidance = Result<TaxReturnDto>.Failure(
                $"No tax return mapper is registered for {country}/{formCode}. Load the country pack (e.g. Tnzi.Finance.Tax.Ca for CA/GST34).", 501);
            return guidance.ToApiResult();
        }

        var result = await mapper.MapAsync(from, to);
        return result.ToApiResult();
    }
}
