namespace Tnzi.Finance.Recurring.Controllers.Admin;

/// <summary>
/// 周期性单据管理
/// </summary>
[DefaultController]
[ApiController]
[Route("admin/finance/recurring")]
[ApiExplorerSettings(GroupName = "admin")]
[ApiAuthorize(PermissionName = "finance.recurring.view")]
public class DefaultFinanceRecurringAdminController : ApiAdminControllerBase
{
    private readonly IRecurringDocumentService _service;
    private readonly IRecurringGeneratorService _generator;

    public DefaultFinanceRecurringAdminController(
        IRecurringDocumentService service,
        IRecurringGeneratorService generator)
    {
        _service = Check.NotNull(service);
        _generator = Check.NotNull(generator);
    }

    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<RecurringDocumentDto>>> GetPaged([FromQuery] RecurringDocumentQueryDto query)
        => (await _service.GetPagedAsync(query)).ToApiResult();

    [HttpGet("{id}")]
    public virtual async Task<ApiResult<RecurringDocumentDto>> Get(Guid id)
        => (await _service.GetAsync(id)).ToApiResult();

    /// <summary>接下来几期分别落在哪天（只读推演，不写任何东西）</summary>
    [HttpGet("{id}/preview")]
    public virtual async Task<ApiResult<RecurrencePreviewDto>> Preview(Guid id, [FromQuery] int count = 6)
        => (await _service.PreviewAsync(id, count)).ToApiResult();

    /// <summary>
    /// 按排期参数直接推演（模板尚未保存时用）。
    /// </summary>
    /// <remarks>
    /// POST 只因为入参是一个对象，**零副作用**；跟随类级 <c>finance.recurring.view</c>，
    /// 无写码。锚点 31 号、每季度、二月怎么算 —— 让人先看见日期再保存。
    /// </remarks>
    [HttpPost("preview")]
    public virtual ApiResult<RecurrencePreviewDto> PreviewSchedule([FromBody] CreateRecurringDocumentDto input, [FromQuery] int count = 6)
        => _service.PreviewSchedule(input, count).ToApiResult();

    [HttpGet("runs")]
    public virtual async Task<ApiResult<IPagedList<RecurringRunDto>>> GetRuns([FromQuery] RecurringRunQueryDto query)
        => (await _service.GetRunsAsync(query)).ToApiResult();

    [HttpPost]
    [ApiAuthorize(PermissionName = "finance.recurring.create")]
    public virtual async Task<ApiResult<RecurringDocumentDto>> Create([FromBody] CreateRecurringDocumentDto input)
        => (await _service.CreateAsync(input)).ToApiResult();

    [HttpPut("{id}")]
    [ApiAuthorize(PermissionName = "finance.recurring.update")]
    public virtual async Task<ApiResult<RecurringDocumentDto>> Update(Guid id, [FromBody] UpdateRecurringDocumentDto input)
        => (await _service.UpdateAsync(id, input)).ToApiResult();

    [HttpDelete("{id}")]
    [ApiAuthorize(PermissionName = "finance.recurring.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
        => (await _service.DeleteAsync(id)).ToApiResult();

    [HttpPost("{id}/pause")]
    [ApiAuthorize(PermissionName = "finance.recurring.update")]
    public virtual async Task<ApiResult<RecurringDocumentDto>> Pause(Guid id)
        => (await _service.PauseAsync(id)).ToApiResult();

    [HttpPost("{id}/resume")]
    [ApiAuthorize(PermissionName = "finance.recurring.update")]
    public virtual async Task<ApiResult<RecurringDocumentDto>> Resume(Guid id)
        => (await _service.ResumeAsync(id)).ToApiResult();

    [HttpPost("{id}/end")]
    [ApiAuthorize(PermissionName = "finance.recurring.update")]
    public virtual async Task<ApiResult<RecurringDocumentDto>> End(Guid id)
        => (await _service.EndAsync(id)).ToApiResult();

    /// <summary>
    /// 立即生成这一条到期的期次。
    /// </summary>
    /// <remarks>
    /// 独立权限码 <c>finance.recurring.execute</c>：它会立刻造出真单据（配置为自动
    /// 过账时还会直接进总账），与"改一条模板"不是同一个动作。重复点击安全 ——
    /// 幂等键挡住同一期次的第二次生成。
    /// </remarks>
    [HttpPost("{id}/run")]
    [ApiAuthorize(PermissionName = "finance.recurring.execute")]
    public virtual async Task<ApiResult<RecurringSweepResultDto>> Run(Guid id, [FromQuery] DateTime? asOf = null)
        => (await _generator.RunOneAsync(id, asOf)).ToApiResult();

    /// <summary>扫描并生成全部到期模板（与后台作业走同一条路径）</summary>
    [HttpPost("run-due")]
    [ApiAuthorize(PermissionName = "finance.recurring.execute")]
    public virtual async Task<ApiResult<RecurringSweepResultDto>> RunDue([FromQuery] DateTime? asOf = null)
        => (await _generator.RunDueAsync(asOf)).ToApiResult();
}
