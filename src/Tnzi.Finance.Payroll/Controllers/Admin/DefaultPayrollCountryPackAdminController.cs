namespace Tnzi.Finance.Payroll.Controllers.Admin;

/// <summary>
/// Country pack 管理控制器（列出已注册的薪酬包 + 按 Code 触发幂等播种）
/// </summary>
[Route("admin/payroll/country-packs")]
[DefaultController]
[ApiAuthorize(PermissionName = "payroll.view")]
public class DefaultPayrollCountryPackAdminController : ApiAdminControllerBase
{
    private readonly ICountryPackService _countryPackService;

    public DefaultPayrollCountryPackAdminController(ICountryPackService countryPackService)
    {
        _countryPackService = Check.NotNull(countryPackService);
    }

    protected ICountryPackService CountryPackService => _countryPackService;

    /// <summary>
    /// 列出已注册的 country pack
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<List<CountryPackDto>>> GetRegistered()
    {
        var result = await _countryPackService.GetRegisteredAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// 按 Code 触发某 pack 的幂等播种
    /// </summary>
    [HttpPost("{code}/seed")]
    [ApiAuthorize(PermissionName = "payroll.pack.execute")]
    public virtual async Task<ApiResult<CountryPackSeedResult>> Seed(string code)
    {
        var result = await _countryPackService.SeedAsync(code);
        return result.ToApiResult();
    }
}
