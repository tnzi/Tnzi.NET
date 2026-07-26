namespace Tnzi.Finance.Controllers.Admin;

/// <summary>
/// 对账单与催收工作台
/// </summary>
/// <remarks>
/// 出文档的端点可选注入 <see cref="IStatementRenderer"/>：未加载
/// <c>Tnzi.Finance.Documents</c> 且未自注册实现时返回 501 引导，**数据端点照常可用**
/// ——与支票渲染、收据提取同一范式。
/// </remarks>
[Route("admin/finance/statements")]
[DefaultController]
[ApiAuthorize(PermissionName = "finance.statement.view")]
public class DefaultFinanceStatementAdminController : ApiAdminControllerBase
{
    private readonly ICustomerStatementService _service;
    private readonly IStatementRenderer? _renderer;

    public DefaultFinanceStatementAdminController(ICustomerStatementService service, IStatementRenderer? renderer = null)
    {
        _service = Check.NotNull(service);
        _renderer = renderer;
    }

    protected ICustomerStatementService Service => _service;

    /// <summary>
    /// 出一张对账单（数据）
    /// </summary>
    [HttpGet("{partyType}/{partyId:guid}")]
    public virtual async Task<ApiResult<CustomerStatementDto>> Get(
        FinancePartyType partyType, Guid partyId, [FromQuery] CustomerStatementQueryDto query)
    {
        var result = await _service.GetAsync(partyType, partyId, query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 出一张对账单（可打印文档）
    /// </summary>
    [HttpGet("{partyType}/{partyId:guid}/document")]
    public virtual async Task<IActionResult> Render(
        FinancePartyType partyType, Guid partyId, [FromQuery] CustomerStatementQueryDto query)
    {
        if (_renderer == null)
        {
            var guidance = Result.Failure(
                "No statement renderer is registered. Load Tnzi.Finance.Documents, or register your own IStatementRenderer.", 501);
            return StatusCode(501, guidance.ToApiResult());
        }

        var data = await _service.GetAsync(partyType, partyId, query);
        if (!data.Succeeded)
            return StatusCode(data.Code ?? 400, data.ToApiResult());

        var rendered = await _renderer.RenderAsync(data.Data!);
        if (!rendered.Succeeded)
            return StatusCode(rendered.Code ?? 500, rendered.ToApiResult());

        var name = $"statement-{data.Data!.PartyName}-{data.Data.PeriodTo:yyyyMMdd}{_renderer.FileExtension}";
        return File(rendered.Data!, _renderer.ContentType, name);
    }

    /// <summary>
    /// 该催谁：按逾期程度倒序
    /// </summary>
    [HttpGet("{partyType}/dunning")]
    public virtual async Task<ApiResult<List<DunningCandidateDto>>> Dunning(FinancePartyType partyType, [FromQuery] DateTime? asOf)
    {
        var result = await _service.GetDunningCandidatesAsync(partyType, asOf);
        return result.ToApiResult();
    }
}
