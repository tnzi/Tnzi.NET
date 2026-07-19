namespace Tnzi.Finance.Payroll.Controllers.Admin;

/// <summary>
/// 发薪批次管理控制器（草稿 CRUD + 计算/过账/付款/作废生命周期 + 工资单子资源 + 外部摄取）
/// </summary>
[Route("admin/payroll/runs")]
[DefaultController]
[ApiAuthorize(PermissionName = "payroll.run.view")]
public class DefaultPayrollPayRunAdminController : ApiAdminControllerBase
{
    private readonly IPayRunService _payRunService;

    public DefaultPayrollPayRunAdminController(IPayRunService payRunService)
    {
        _payRunService = Check.NotNull(payRunService);
    }

    protected IPayRunService PayRunService => _payRunService;

    /// <summary>
    /// 分页查询发薪批次
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<PayRunListDto>>> GetPaged([FromQuery] PayRunQueryDto query)
    {
        var result = await _payRunService.GetPagedAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取发薪批次
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<PayRunDto>> Get(Guid id)
    {
        var result = await _payRunService.GetAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建发薪批次草稿
    /// </summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "payroll.run.create")]
    public virtual async Task<ApiResult<PayRunDto>> Create([FromBody] CreatePayRunDto request)
    {
        var result = await _payRunService.CreateAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新发薪批次草稿
    /// </summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "payroll.run.update")]
    public virtual async Task<ApiResult<PayRunDto>> Update(Guid id, [FromBody] UpdatePayRunDto request)
    {
        var result = await _payRunService.UpdateAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除发薪批次草稿
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "payroll.run.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await _payRunService.DeleteAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 计算/重算发薪批次
    /// </summary>
    [HttpPost("{id:guid}/calculate")]
    [ApiAuthorize(PermissionName = "payroll.run.update")]
    public virtual async Task<ApiResult<PayRunDto>> Calculate(Guid id)
    {
        var result = await _payRunService.CalculateAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 过账发薪批次
    /// </summary>
    [HttpPost("{id:guid}/post")]
    [ApiAuthorize(PermissionName = "payroll.run.update")]
    public virtual async Task<ApiResult<PayRunDto>> Post(Guid id)
    {
        var result = await _payRunService.PostAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 付款
    /// </summary>
    [HttpPost("{id:guid}/pay")]
    [ApiAuthorize(PermissionName = "payroll.run.update")]
    public virtual async Task<ApiResult<PayRunDto>> Pay(Guid id, [FromBody] PayRunPaymentDto request)
    {
        var result = await _payRunService.PayAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 作废发薪批次
    /// </summary>
    [HttpPost("{id:guid}/void")]
    [ApiAuthorize(PermissionName = "payroll.run.update")]
    public virtual async Task<ApiResult<PayRunDto>> Void(Guid id)
    {
        var result = await _payRunService.VoidAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 列出批次的工资单
    /// </summary>
    [HttpGet("{id:guid}/payslips")]
    public virtual async Task<ApiResult<List<PayslipListDto>>> GetPayslips(Guid id)
    {
        var result = await _payRunService.GetPayslipsAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取单张工资单（含行）
    /// </summary>
    [HttpGet("{id:guid}/payslips/{payslipId:guid}")]
    public virtual async Task<ApiResult<PayslipDto>> GetPayslip(Guid id, Guid payslipId)
    {
        var result = await _payRunService.GetPayslipAsync(id, payslipId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 修改单张工资单输入并单独重算
    /// </summary>
    [HttpPut("{id:guid}/payslips/{payslipId:guid}/inputs")]
    [ApiAuthorize(PermissionName = "payroll.run.update")]
    public virtual async Task<ApiResult<PayslipDto>> UpdatePayslipInputs(Guid id, Guid payslipId, [FromBody] UpdatePayslipInputsDto request)
    {
        var result = await _payRunService.UpdatePayslipInputsAsync(id, payslipId, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 外部批次幂等摄取（External/OpeningBalance）
    /// </summary>
    [HttpPost("external")]
    [ApiAuthorize(PermissionName = "payroll.run.create")]
    public virtual async Task<ApiResult<PayRunDto>> CreateFromExternal([FromBody] ExternalPayRunIngestDto request)
    {
        var result = await _payRunService.CreateFromExternalAsync(request);
        return result.ToApiResult();
    }
}
