namespace Tnzi.Finance.Controllers.Admin;

/// <summary>
/// EFT 批次管理控制器
/// </summary>
/// <remarks>
/// 下载端点用独立权限码 <c>finance.eft.download</c>：生成文件含全量明文账号，与只读 <c>finance.eft.view</c> 分离。
/// </remarks>
[Route("admin/finance/eft-batches")]
[DefaultController]
[ApiAuthorize(PermissionName = "finance.eft.view")]
public class DefaultFinanceEftBatchAdminController : ApiAdminControllerBase
{
    private readonly IEftService _service;

    public DefaultFinanceEftBatchAdminController(IEftService service)
    {
        _service = Check.NotNull(service);
    }

    protected IEftService Service => _service;

    /// <summary>可入批队列</summary>
    [HttpGet("queue")]
    public virtual async Task<ApiResult<List<EftQueueItemDto>>> GetQueue()
    {
        var result = await _service.GetQueueAsync();
        return result.ToApiResult();
    }

    /// <summary>分页查询批次</summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<EftBatchDto>>> GetPaged([FromQuery] EftBatchQueryDto query)
    {
        var result = await _service.GetPagedAsync(query);
        return result.ToApiResult();
    }

    /// <summary>获取批次（含行）</summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<EftBatchDto>> Get(Guid id)
    {
        var result = await _service.GetAsync(id);
        return result.ToApiResult();
    }

    /// <summary>创建草稿批次</summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "finance.eft.create")]
    public virtual async Task<ApiResult<EftBatchDto>> Create([FromBody] CreateEftBatchDto request)
    {
        var result = await _service.CreateBatchAsync(request);
        return result.ToApiResult();
    }

    /// <summary>生成文件</summary>
    [HttpPost("{id:guid}/generate")]
    [ApiAuthorize(PermissionName = "finance.eft.update")]
    public virtual async Task<ApiResult<EftBatchDto>> Generate(Guid id)
    {
        var result = await _service.GenerateAsync(id);
        return result.ToApiResult();
    }

    /// <summary>作废批次</summary>
    [HttpPost("{id:guid}/void")]
    [ApiAuthorize(PermissionName = "finance.eft.update")]
    public virtual async Task<ApiResult<EftBatchDto>> Void(Guid id, [FromBody] VoidEftBatchDto request)
    {
        var result = await _service.VoidBatchAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>下载生成的 EFT 文件（含明文账号，独立权限码）</summary>
    [HttpGet("{id:guid}/download")]
    [ApiAuthorize(PermissionName = "finance.eft.download")]
    public virtual async Task<IActionResult> Download(Guid id)
    {
        var result = await _service.DownloadAsync(id);
        if (!result.Succeeded)
            return StatusCode(result.Code ?? 400, result.ToApiResult());
        return File(result.Data!.Content, "text/plain", result.Data.FileName);
    }
}
