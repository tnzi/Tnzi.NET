namespace Tnzi.Finance.Banking.Controllers.Admin;

/// <summary>
/// 收据采集管理控制器
/// </summary>
/// <remarks>
/// 上传经 Storage 既有用户端上传拿到 fileId（Chat 附件同模式），再 <c>POST</c> 登记；
/// 转换走 <c>finance.document.create</c>（复用单据创建码）。
/// </remarks>
[Route("admin/finance/receipts")]
[DefaultController]
[ApiAuthorize(PermissionName = "finance.receipt.view")]
public class DefaultFinanceReceiptAdminController : ApiAdminControllerBase
{
    private readonly IReceiptCaptureService _service;

    public DefaultFinanceReceiptAdminController(IReceiptCaptureService service)
    {
        _service = Check.NotNull(service);
    }

    protected IReceiptCaptureService Service => _service;

    /// <summary>分页查询收据</summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<ReceiptDto>>> GetPaged([FromQuery] ReceiptQueryDto query)
    {
        var result = await _service.GetPagedAsync(query);
        return result.ToApiResult();
    }

    /// <summary>获取收据</summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<ReceiptDto>> Get(Guid id)
    {
        var result = await _service.GetAsync(id);
        return result.ToApiResult();
    }

    /// <summary>登记收据（上传后 fileId）</summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "finance.receipt.create")]
    public virtual async Task<ApiResult<ReceiptDto>> Create([FromBody] CreateReceiptDto request)
    {
        var result = await _service.CreateAsync(request);
        return result.ToApiResult();
    }

    /// <summary>提取字段</summary>
    [HttpPost("{id:guid}/extract")]
    [ApiAuthorize(PermissionName = "finance.receipt.update")]
    public virtual async Task<ApiResult<ReceiptDto>> Extract(Guid id)
    {
        var result = await _service.ExtractAsync(id);
        return result.ToApiResult();
    }

    /// <summary>人工修正提取字段</summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.receipt.update")]
    public virtual async Task<ApiResult<ReceiptDto>> Update(Guid id, [FromBody] UpdateReceiptExtractionDto request)
    {
        var result = await _service.UpdateExtractionAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>转换为费用/账单草稿（权限走 finance.document.create）</summary>
    [HttpPost("{id:guid}/convert")]
    [ApiAuthorize(PermissionName = "finance.document.create")]
    public virtual async Task<ApiResult<ReceiptConvertResultDto>> Convert(Guid id, [FromBody] ConvertReceiptDto request)
    {
        var result = await _service.ConvertAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>删除收据（Converted 拒绝）</summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.receipt.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await _service.DeleteAsync(id);
        return result.ToApiResult();
    }
}
