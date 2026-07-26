namespace Tnzi.Finance.Banking.Controllers.Admin;

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
        return RenderedFile(result);
    }

    /// <summary>
    /// 预览支票（零副作用：不分配支票号、不写登记簿、不动账）
    /// </summary>
    /// <remarks>
    /// 只读语义，故跟随类级 <c>finance.check.view</c> 门（无写码）。POST 仅因为入参是一组付款单 id。
    /// 呈现端可把返回的文档直接塞进 iframe 做所见即所得预览。
    /// </remarks>
    [HttpPost("preview")]
    public virtual async Task<IActionResult> Preview([FromBody] PreviewChecksDto request)
    {
        var result = await _service.PreviewAsync(request);
        return RenderedFile(result);
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
        return RenderedFile(result);
    }

    /// <summary>登记手工支票</summary>
    [HttpPost("register")]
    [ApiAuthorize(PermissionName = "finance.check.create")]
    public virtual async Task<ApiResult<BankCheckDto>> Register([FromBody] RegisterManualCheckDto request)
    {
        var result = await _service.RegisterManualAsync(request);
        return result.ToApiResult();
    }

    /// <summary>重打支票（作废原票 + 新票，合并文档下载）</summary>
    [HttpPost("{id:guid}/reprint")]
    [ApiAuthorize(PermissionName = "finance.check.create")]
    public virtual async Task<IActionResult> Reprint(Guid id)
    {
        var result = await _service.ReprintAsync(id);
        return RenderedFile(result);
    }

    /// <summary>重新渲染已开支票（同号重打，零副作用）</summary>
    /// <remarks>
    /// 用于"票据已开出但纸没打成"（打印机卡纸 / 操作员关掉了打印对话框）。虽然不写库，
    /// 但它产出的是<b>可流通票据</b>，与"看一眼登记簿"不是一个风险等级，故走写码
    /// <c>finance.check.create</c>；用 POST 而非 GET 同理（非幂等的现实副作用在纸上）。
    /// </remarks>
    [HttpPost("{id:guid}/render")]
    [ApiAuthorize(PermissionName = "finance.check.create")]
    public virtual async Task<IActionResult> Render(Guid id)
    {
        var result = await _service.RenderAsync(id);
        return RenderedFile(result);
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

    /// <summary>
    /// 把渲染结果落地为文件下载（失败按 Result.Code 返回 ApiResult 信封）
    /// </summary>
    /// <remarks>
    /// MIME 取自 <see cref="CheckFileDto.ContentType"/>——生效的渲染器决定输出格式
    /// （模板驱动 → <c>text/html</c>，PdfSharp → <c>application/pdf</c>）。
    /// </remarks>
    private IActionResult RenderedFile(Result<CheckFileDto> result)
    {
        if (!result.Succeeded)
            return StatusCode(result.Code ?? 400, result.ToApiResult());
        return File(result.Data!.Content, result.Data.ContentType, result.Data.FileName);
    }
}
