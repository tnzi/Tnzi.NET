namespace Tnzi.Finance.Controllers.Admin;

/// <summary>
/// 会计凭证管理控制器
/// </summary>
[Route("admin/finance/journal-entries")]
[DefaultController]
[ApiAuthorize(PermissionName = "finance.journal.view")]
public class DefaultFinanceJournalEntryAdminController : ApiAdminControllerBase
{
    private readonly IJournalEntryService _journalEntryService;

    public DefaultFinanceJournalEntryAdminController(IJournalEntryService journalEntryService)
    {
        _journalEntryService = Check.NotNull(journalEntryService);
    }

    protected IJournalEntryService JournalEntryService => _journalEntryService;

    /// <summary>
    /// 分页查询凭证
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<JournalEntryDto>>> GetList([FromQuery] JournalEntryQueryDto query)
    {
        var result = await _journalEntryService.GetListAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取凭证（含分录行）
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<JournalEntryDto>> Get(Guid id)
    {
        var result = await _journalEntryService.GetAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建凭证草稿
    /// </summary>
    [HttpPost]
    public virtual async Task<ApiResult<JournalEntryDto>> CreateDraft([FromBody] CreateJournalEntryDto request)
    {
        var result = await _journalEntryService.CreateDraftAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新凭证草稿
    /// </summary>
    [HttpPut("{id:guid}")]
    public virtual async Task<ApiResult<JournalEntryDto>> UpdateDraft(Guid id, [FromBody] CreateJournalEntryDto request)
    {
        var result = await _journalEntryService.UpdateDraftAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除凭证草稿
    /// </summary>
    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> DeleteDraft(Guid id)
    {
        var result = await _journalEntryService.DeleteDraftAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 过账
    /// </summary>
    [HttpPost("{id:guid}/post")]
    public virtual async Task<ApiResult<JournalEntryDto>> Post(Guid id)
    {
        var result = await _journalEntryService.PostAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 冲销
    /// </summary>
    [HttpPost("{id:guid}/reverse")]
    public virtual async Task<ApiResult<JournalEntryDto>> Reverse(Guid id, [FromBody] ReverseJournalEntryDto? request)
    {
        var result = await _journalEntryService.ReverseAsync(id, request ?? new ReverseJournalEntryDto());
        return result.ToApiResult();
    }
}
