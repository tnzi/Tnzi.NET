namespace Tnzi.Finance.Controllers.Admin;

/// <summary>
/// 支票打印与登记管理控制器
/// </summary>
[Route("admin/finance/checks")]
[DefaultController]
[ApiAuthorize(PermissionName = "finance.check.view")]
public class DefaultFinanceCheckAdminController : ApiAdminControllerBase
{
    private readonly ICheckService _service;

    public DefaultFinanceCheckAdminController(ICheckService service)
    {
        _service = Check.NotNull(service);
    }

    protected ICheckService Service => _service;

    /// <summary>打印队列</summary>
    [HttpGet("queue")]
    public virtual async Task<ApiResult<List<CheckQueueItemDto>>> GetQueue([FromQuery] Guid? bankAccountId)
    {
        var result = await _service.GetQueueAsync(bankAccountId);
        return result.ToApiResult();
    }

    /// <summary>分页查询支票登记簿</summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<BankCheckDto>>> GetPaged([FromQuery] CheckQueryDto query)
    {
        var result = await _service.GetPagedAsync(query);
        return result.ToApiResult();
    }

    /// <summary>生成校准标尺测试页</summary>
    [HttpGet("calibration/{bankAccountId:guid}")]
    public virtual async Task<IActionResult> Calibration(Guid bankAccountId)
    {
        var result = await _service.GetCalibrationPdfAsync(bankAccountId);
        return PdfResult(result);
    }

    /// <summary>positive-pay 已开票文件 CSV 导出（某银行账户 [from,to] 签发日窗口）</summary>
    [HttpGet("positive-pay/{bankAccountId:guid}/export")]
    public virtual async Task<IActionResult> ExportPositivePay(Guid bankAccountId, [FromQuery] DateTime from, [FromQuery] DateTime to)
        => CsvFile(await _service.ExportPositivePayAsync(bankAccountId, from, to), "positive_pay");

    /// <summary>打印支票（合并 PDF 下载）</summary>
    [HttpPost("print")]
    [ApiAuthorize(PermissionName = "finance.check.create")]
    public virtual async Task<IActionResult> Print([FromBody] PrintChecksDto request)
    {
        var result = await _service.PrintAsync(request);
        return PdfResult(result);
    }

    /// <summary>登记手工支票</summary>
    [HttpPost("register")]
    [ApiAuthorize(PermissionName = "finance.check.create")]
    public virtual async Task<ApiResult<BankCheckDto>> Register([FromBody] RegisterManualCheckDto request)
    {
        var result = await _service.RegisterManualAsync(request);
        return result.ToApiResult();
    }

    /// <summary>重打支票（作废原票 + 新票，合并 PDF 下载）</summary>
    [HttpPost("{id:guid}/reprint")]
    [ApiAuthorize(PermissionName = "finance.check.create")]
    public virtual async Task<IActionResult> Reprint(Guid id)
    {
        var result = await _service.ReprintAsync(id);
        return PdfResult(result);
    }

    /// <summary>作废支票</summary>
    [HttpPost("{id:guid}/void")]
    [ApiAuthorize(PermissionName = "finance.check.update")]
    public virtual async Task<ApiResult<BankCheckDto>> Void(Guid id, [FromBody] VoidCheckDto request)
    {
        var result = await _service.VoidAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>登记毁票</summary>
    [HttpPost("spoil")]
    [ApiAuthorize(PermissionName = "finance.check.update")]
    public virtual async Task<ApiResult<BankCheckDto>> Spoil([FromBody] SpoilCheckDto request)
    {
        var result = await _service.SpoilAsync(request);
        return result.ToApiResult();
    }

    /// <summary>把渲染结果落地为 PDF 下载（失败按 Result.Code 返回 ApiResult 信封）</summary>
    private IActionResult PdfResult(Result<CheckFileDto> result)
    {
        if (!result.Succeeded)
            return StatusCode(result.Code ?? 400, result.ToApiResult());
        return File(result.Data!.Content, "application/pdf", result.Data.FileName);
    }
}
