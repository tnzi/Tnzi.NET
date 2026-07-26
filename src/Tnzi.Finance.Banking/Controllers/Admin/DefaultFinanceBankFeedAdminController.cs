using System.IO;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Tnzi.Finance.Banking.Controllers.Admin;

/// <summary>
/// 银行流水导入与匹配管理控制器
/// </summary>
[Route("admin/finance/bank-feed")]
[DefaultController]
[ApiAuthorize(PermissionName = "finance.bankFeed.view")]
public class DefaultFinanceBankFeedAdminController : ApiAdminControllerBase
{
    private static readonly JsonSerializerOptions MappingJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IBankFeedService _service;

    public DefaultFinanceBankFeedAdminController(IBankFeedService service)
    {
        _service = Check.NotNull(service);
    }

    protected IBankFeedService Service => _service;

    /// <summary>分页查询银行流水</summary>
    [HttpGet("transactions")]
    public virtual async Task<ApiResult<IPagedList<BankTransactionDto>>> GetPaged([FromQuery] BankTransactionQueryDto query)
    {
        var result = await _service.GetPagedAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 导入对账单文件（OFX / CSV）。multipart：file + source + 可选 mapping（CSV 列映射 JSON）。
    /// </summary>
    [HttpPost("import")]
    [ApiAuthorize(PermissionName = "finance.bankFeed.create")]
    public virtual async Task<ApiResult<BankImportResultDto>> Import(
        [FromForm] Guid accountId,
        [FromForm] BankTransactionSource source,
        IFormFile? file,
        [FromForm] string? mapping,
        CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
            return Result<BankImportResultDto>.Failure("A statement file is required.", 400).ToApiResult();

        // 银行流水（OFX/CSV）是文本文件，20MB 上限；在整文件读入内存前先按 Length 拦截超大上传，防内存尖峰
        const long maxImportBytes = 20L * 1024 * 1024;
        if (file.Length > maxImportBytes)
            return Result<BankImportResultDto>.Failure($"The statement file exceeds the {maxImportBytes / (1024 * 1024)} MB import limit.", 400).ToApiResult();

        CsvMappingDto? mappingDto = null;
        if (!string.IsNullOrWhiteSpace(mapping))
        {
            try
            {
                mappingDto = JsonSerializer.Deserialize<CsvMappingDto>(mapping, MappingJsonOptions);
            }
            catch (JsonException)
            {
                return Result<BankImportResultDto>.Failure("The CSV mapping is not valid JSON.", 400).ToApiResult();
            }
        }

        string content;
        using (var reader = new StreamReader(file.OpenReadStream()))
        {
            content = await reader.ReadToEndAsync(cancellationToken);
        }

        var result = await _service.ImportStatementAsync(accountId, source, file.FileName, content, mappingDto, cancellationToken);
        return result.ToApiResult();
    }

    /// <summary>从银行 feed 提供者拉取</summary>
    [HttpPost("pull")]
    [ApiAuthorize(PermissionName = "finance.bankFeed.create")]
    public virtual async Task<ApiResult<BankImportResultDto>> Pull([FromBody] PullBankFeedDto request)
    {
        var result = await _service.PullFromProviderAsync(request);
        return result.ToApiResult();
    }

    /// <summary>对该科目全部待匹配流水跑匹配引擎</summary>
    [HttpPost("suggest")]
    [ApiAuthorize(PermissionName = "finance.bankFeed.update")]
    public virtual async Task<ApiResult<BankSuggestResultDto>> Suggest([FromQuery] Guid accountId)
    {
        var result = await _service.SuggestMatchesAsync(accountId);
        return result.ToApiResult();
    }

    /// <summary>列出某流水的匹配候选</summary>
    [HttpGet("transactions/{id:guid}/candidates")]
    public virtual async Task<ApiResult<List<BankMatchCandidateDto>>> GetCandidates(Guid id)
    {
        var result = await _service.GetCandidatesAsync(id);
        return result.ToApiResult();
    }

    /// <summary>确认匹配（生成当前 Draft 对账勾选行）</summary>
    [HttpPost("transactions/{id:guid}/confirm")]
    [ApiAuthorize(PermissionName = "finance.bankFeed.update")]
    public virtual async Task<ApiResult<BankTransactionDto>> Confirm(Guid id, [FromBody] ConfirmBankMatchDto request)
    {
        var result = await _service.ConfirmMatchAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>撤销匹配</summary>
    [HttpPost("transactions/{id:guid}/unmatch")]
    [ApiAuthorize(PermissionName = "finance.bankFeed.update")]
    public virtual async Task<ApiResult<BankTransactionDto>> Unmatch(Guid id)
    {
        var result = await _service.UnmatchAsync(id);
        return result.ToApiResult();
    }

    /// <summary>排除流水</summary>
    [HttpPost("transactions/{id:guid}/exclude")]
    [ApiAuthorize(PermissionName = "finance.bankFeed.update")]
    public virtual async Task<ApiResult<BankTransactionDto>> Exclude(Guid id)
    {
        var result = await _service.ExcludeAsync(id);
        return result.ToApiResult();
    }

    /// <summary>恢复被排除的流水</summary>
    [HttpPost("transactions/{id:guid}/restore")]
    [ApiAuthorize(PermissionName = "finance.bankFeed.update")]
    public virtual async Task<ApiResult<BankTransactionDto>> Restore(Guid id)
    {
        var result = await _service.RestoreAsync(id);
        return result.ToApiResult();
    }

    /// <summary>由流水创建单据草稿（权限走 finance.document.create）</summary>
    [HttpPost("transactions/{id:guid}/create-document")]
    [ApiAuthorize(PermissionName = "finance.document.create")]
    public virtual async Task<ApiResult<BankDocumentResultDto>> CreateDocument(Guid id, [FromBody] CreateBankDocumentDto request)
    {
        var result = await _service.CreateDocumentAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>分页查询导入批次</summary>
    [HttpGet("batches")]
    public virtual async Task<ApiResult<IPagedList<BankImportBatchDto>>> GetBatches([FromQuery] BankImportBatchQueryDto query)
    {
        var result = await _service.GetBatchesAsync(query);
        return result.ToApiResult();
    }

    /// <summary>撤销导入批次</summary>
    [HttpDelete("batches/{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.bankFeed.delete")]
    public virtual async Task<ApiResult> DeleteBatch(Guid id)
    {
        var result = await _service.DeleteBatchAsync(id);
        return result.ToApiResult();
    }
}
