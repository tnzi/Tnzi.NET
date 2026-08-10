using System.Text.Json;

namespace Tnzi.Finance.Banking.Services;

/// <summary>
/// 收据采集服务
/// </summary>
/// <remarks>
/// <see cref="IReceiptExtractor"/> 可选注入：消费应用未注册实现时为 null，
/// <see cref="ExtractAsync"/> 返回 501 引导。转换委托既有 <c>IExpenseService</c>/<c>IBillService.CreateDraftAsync</c>，
/// 产出止步草稿；并发双 convert 由 <see cref="Receipt.ConcurrencyStamp"/> 挡 409。
/// </remarks>
public class ReceiptCaptureService : ApplicationService, IReceiptCaptureService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IRepository<Receipt, Guid> _receiptRepository;
    private readonly IReadOnlyRepository<Vendor, Guid> _vendorRepository;
    private readonly IExpenseService _expenseService;
    private readonly IBillService _billService;
    private readonly IReceiptExtractor? _extractor;

    public ReceiptCaptureService(
        IServiceProvider serviceProvider,
        IRepository<Receipt, Guid> receiptRepository,
        IReadOnlyRepository<Vendor, Guid> vendorRepository,
        IExpenseService expenseService,
        IBillService billService,
        IReceiptExtractor? extractor = null)
        : base(serviceProvider)
    {
        _receiptRepository = Check.NotNull(receiptRepository);
        _vendorRepository = Check.NotNull(vendorRepository);
        _expenseService = Check.NotNull(expenseService);
        _billService = Check.NotNull(billService);
        _extractor = extractor;
    }

    public async Task<Result<IPagedList<ReceiptDto>>> GetPagedAsync(ReceiptQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var queryable = _receiptRepository.AsNoTracking();
        if (query.Status.HasValue)
            queryable = queryable.Where(r => r.Status == query.Status.Value);
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.ToLower();
            queryable = queryable.Where(r =>
                (r.VendorName != null && r.VendorName.ToLower().Contains(keyword)) ||
                (r.OriginalFileName != null && r.OriginalFileName.ToLower().Contains(keyword)) ||
                (r.Reference != null && r.Reference.ToLower().Contains(keyword)));
        }

        var pagedList = await queryable
            .OrderByDescending(r => r.CreationTime)
            .ProjectTo<Receipt, ReceiptDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        await FillVendorNamesAsync(pagedList.Items, cancellationToken);
        return Ok(pagedList);
    }

    public async Task<Result<ReceiptDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var receipt = await _receiptRepository.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (receipt == null)
            return Fail<ReceiptDto>("Receipt not found.", 404);

        return Ok(await ToDtoAsync(receipt, cancellationToken));
    }

    public async Task<Result<ReceiptDto>> CreateAsync(CreateReceiptDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);
        if (input.FileId == Guid.Empty)
            return Fail<ReceiptDto>("A file id is required.", 400);

        // 人打的值越界一律 400 并点名字段：悄悄截断会让他看到自己没输入过的内容而没有解释。
        var invalid = ReceiptFieldLimits.ValidateUserInput(input.FileName, vendorName: null, input.Currency, reference: null);
        if (invalid != null)
            return Fail<ReceiptDto>(invalid, 400);

        var receipt = new Receipt
        {
            FileId = input.FileId,
            OriginalFileName = input.FileName,
            Currency = string.IsNullOrWhiteSpace(input.Currency) ? null : input.Currency.Trim().ToUpperInvariant(),
            Status = ReceiptStatus.Uploaded
        };

        await _receiptRepository.InsertAsync(receipt, cancellationToken);
        await _receiptRepository.SaveChangesAsync(cancellationToken);

        return await GetAsync(receipt.Id, cancellationToken);
    }

    public async Task<Result<ReceiptDto>> ExtractAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (_extractor == null)
            return Fail<ReceiptDto>("Receipt extraction requires an IReceiptExtractor implementation. Register one in your application (see the Finance module docs for a vision-based recipe).", 501);

        var receipt = await _receiptRepository.AsQueryable(true).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (receipt == null)
            return Fail<ReceiptDto>("Receipt not found.", 404);
        if (receipt.Status == ReceiptStatus.Converted)
            return Fail<ReceiptDto>("A converted receipt cannot be re-extracted.", 409);

        var request = new ReceiptExtractionRequest
        {
            FileId = receipt.FileId,
            FileName = receipt.OriginalFileName,
            HintCurrency = receipt.Currency
        };

        Result<ReceiptExtractionResult> extraction;
        try
        {
            extraction = await _extractor.ExtractAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            // 提取器由消费应用提供，原始异常消息可能含内部细节；记服务端日志，对外只返回通用消息
            Logger.LogError(ex, "Receipt extraction failed for receipt {ReceiptId}.", id);
            extraction = Result<ReceiptExtractionResult>.Failure("Receipt extraction failed. See server logs for details.", 500);
        }

        if (!extraction.Succeeded || extraction.Data == null)
        {
            receipt.Status = ReceiptStatus.Failed;
            receipt.FailReason = ReceiptFieldLimits.TruncateFailReason(extraction.Message);
            await _receiptRepository.UpdateAsync(receipt, cancellationToken);
            await _receiptRepository.SaveChangesAsync(cancellationToken);
            return Fail<ReceiptDto>(receipt.FailReason, extraction.Code ?? 502);
        }

        // 提取器是可替换的扩展点，返回值一律当外部数据看待：越界的字符串会让插入 500，
        // 而未归一化的置信度会在界面上渲染成 9500%。见 ReceiptFieldLimits。
        var data = ReceiptFieldLimits.NormalizeExtraction(extraction.Data, out var adjustments);
        if (adjustments.Count > 0)
        {
            Logger.LogWarning(
                "Receipt {ReceiptId}: the extractor returned values outside the persisted shape ({Adjustments}).",
                id, string.Join("; ", adjustments));
        }

        receipt.VendorName = data.VendorName;
        receipt.DocDate = data.DocDate?.ToUtcDate();
        receipt.Currency = data.Currency ?? receipt.Currency;
        receipt.Subtotal = data.Subtotal;
        receipt.TaxAmount = data.TaxAmount;
        receipt.Total = data.Total;
        receipt.Reference = data.Reference;
        receipt.Confidence = data.Confidence;
        receipt.LineItemsJson = data.LineItems.Count > 0 ? JsonSerializer.Serialize(data.LineItems, JsonOptions) : null;
        receipt.MatchedVendorId = await SuggestVendorAsync(data.VendorName, cancellationToken);
        receipt.Status = ReceiptStatus.Extracted;
        receipt.FailReason = null;

        await _receiptRepository.UpdateAsync(receipt, cancellationToken);
        await _receiptRepository.SaveChangesAsync(cancellationToken);

        await PublishEventAsync(new ReceiptExtractedEvent
        {
            ReceiptId = receipt.Id,
            VendorName = receipt.VendorName,
            Total = receipt.Total,
            Confidence = receipt.Confidence,
            TenantId = receipt.TenantId
        }, cancellationToken);

        return await GetAsync(receipt.Id, cancellationToken);
    }

    public async Task<Result<ReceiptDto>> UpdateExtractionAsync(Guid id, UpdateReceiptExtractionDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var receipt = await _receiptRepository.AsQueryable(true).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (receipt == null)
            return Fail<ReceiptDto>("Receipt not found.", 404);
        if (receipt.Status == ReceiptStatus.Converted)
            return Fail<ReceiptDto>("A converted receipt cannot be edited.", 409);

        var invalid = ReceiptFieldLimits.ValidateUserInput(
            fileName: null, input.VendorName, input.Currency, input.Reference);
        if (invalid != null)
            return Fail<ReceiptDto>(invalid, 400);

        receipt.VendorName = input.VendorName;
        receipt.DocDate = input.DocDate?.ToUtcDate();
        receipt.Currency = string.IsNullOrWhiteSpace(input.Currency) ? null : input.Currency.Trim().ToUpperInvariant();
        receipt.Subtotal = input.Subtotal;
        receipt.TaxAmount = input.TaxAmount;
        receipt.Total = input.Total;
        receipt.Reference = input.Reference;
        receipt.MatchedVendorId = input.MatchedVendorId ?? await SuggestVendorAsync(input.VendorName, cancellationToken);
        if (receipt.Status == ReceiptStatus.Uploaded || receipt.Status == ReceiptStatus.Failed)
            receipt.Status = ReceiptStatus.Extracted;

        try
        {
            await _receiptRepository.UpdateAsync(receipt, cancellationToken);
            await _receiptRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<ReceiptDto>("The receipt was modified by another operation. Reload and retry.", 409);
        }

        return await GetAsync(receipt.Id, cancellationToken);
    }

    public async Task<Result<ReceiptConvertResultDto>> ConvertAsync(Guid id, ConvertReceiptDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var receipt = await _receiptRepository.AsQueryable(true).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (receipt == null)
            return Fail<ReceiptConvertResultDto>("Receipt not found.", 404);
        if (receipt.Status == ReceiptStatus.Converted)
            return Fail<ReceiptConvertResultDto>("The receipt has already been converted.", 409);

        var vendorId = input.VendorId ?? receipt.MatchedVendorId;
        if (vendorId == null)
            return Fail<ReceiptConvertResultDto>("A vendor is required. Match a vendor or pass one explicitly.", 400);
        if (input.AccountId == null)
            return Fail<ReceiptConvertResultDto>("An expense account is required for the draft line.", 400);

        var amount = receipt.Total ?? receipt.Subtotal ?? 0m;
        if (amount <= 0m)
            return Fail<ReceiptConvertResultDto>("The receipt has no amount. Edit the extraction before converting.", 400);

        if (input.DocType == ReceiptDocType.Expense && input.PaidFromAccountId == null)
            return Fail<ReceiptConvertResultDto>("A funding account is required to convert to an expense.", 400);
        if (input.DocType != ReceiptDocType.Expense && input.DocType != ReceiptDocType.Bill)
            return Fail<ReceiptConvertResultDto>("Unsupported document type.", 400);

        var docDate = receipt.DocDate ?? DateTime.UtcNow;
        var currency = receipt.Currency;
        var memo = receipt.VendorName;

        // 原子化：草稿创建（CreateDraftAsync 自提交）与 receipt 状态更新必须在同一工作单元，
        // 否则并发/重试的双 convert（或状态更新失败）会留下已提交的孤儿 Expense/Bill 草稿。
        // 任一步失败或 receipt 乐观并发冲突 → 抛出使整体回滚（含刚建的草稿），对齐 SettlementService 铁律。
        Result<ReceiptConvertResultDto> result;
        try
        {
            result = await ExecuteInUnitOfWorkAsync<Result<ReceiptConvertResultDto>>(async ct =>
            {
                string docType;
                Guid docId;
                if (input.DocType == ReceiptDocType.Expense)
                {
                    var expense = await _expenseService.CreateDraftAsync(new CreateExpenseDto
                    {
                        VendorId = vendorId,
                        PaidFromAccountId = input.PaidFromAccountId!.Value,
                        DocDate = docDate,
                        Currency = currency,
                        Memo = memo,
                        Lines = new List<CreateExpenseLineDto>
                        {
                            new() { AccountId = input.AccountId.Value, Amount = amount, Description = receipt.VendorName }
                        }
                    }, ct);
                    if (!expense.Succeeded)
                        throw new UnitOfWorkAbortException(Result.Failure(expense.Message!, expense.Code ?? 400));
                    docType = FinanceSourceTypes.Expense;
                    docId = expense.Data!.Id;
                }
                else
                {
                    var bill = await _billService.CreateDraftAsync(new CreateBillDto
                    {
                        VendorId = vendorId.Value,
                        DocDate = docDate,
                        Currency = currency,
                        Memo = memo,
                        Lines = new List<CreateBillLineDto>
                        {
                            new() { AccountId = input.AccountId.Value, Quantity = 1m, UnitPrice = amount, Description = receipt.VendorName }
                        }
                    }, ct);
                    if (!bill.Succeeded)
                        throw new UnitOfWorkAbortException(Result.Failure(bill.Message!, bill.Code ?? 400));
                    docType = FinanceSourceTypes.Bill;
                    docId = bill.Data!.Id;
                }

                receipt.Status = ReceiptStatus.Converted;
                receipt.ConvertedDocType = docType;
                receipt.ConvertedDocId = docId;
                if (receipt.MatchedVendorId == null)
                    receipt.MatchedVendorId = vendorId;
                await _receiptRepository.UpdateAsync(receipt, ct);
                await _receiptRepository.SaveChangesAsync(ct);

                return Ok(new ReceiptConvertResultDto { DocType = docType, DocId = docId });
            }, cancellationToken);
        }
        catch (UnitOfWorkAbortException ex)
        {
            return Fail<ReceiptConvertResultDto>(ex.Result.Message ?? "Conversion failed.", ex.Result.Code ?? 400);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<ReceiptConvertResultDto>("The receipt was modified by another operation. Reload and retry.", 409);
        }

        if (result.Succeeded)
        {
            await PublishEventAsync(new ReceiptConvertedEvent
            {
                ReceiptId = receipt.Id,
                DocType = result.Data!.DocType,
                DocId = result.Data.DocId,
                TenantId = receipt.TenantId
            }, cancellationToken);
        }

        return result;
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var receipt = await _receiptRepository.AsQueryable(true).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (receipt == null)
            return Fail("Receipt not found.", 404);
        if (receipt.Status == ReceiptStatus.Converted)
            return Fail("A converted receipt cannot be deleted.", 409);

        await _receiptRepository.DeleteAsync(receipt, cancellationToken);
        return Ok();
    }

    /// <summary>按供应商名称建议匹配（精确优先，其次前缀）。</summary>
    private async Task<Guid?> SuggestVendorAsync(string? vendorName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(vendorName))
            return null;

        var lower = vendorName.Trim().ToLower();
        var exact = await _vendorRepository.AsNoTracking()
            .Where(v => v.Name.ToLower() == lower)
            .Select(v => (Guid?)v.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (exact != null)
            return exact;

        return await _vendorRepository.AsNoTracking()
            .Where(v => v.Name.ToLower().StartsWith(lower))
            .OrderBy(v => v.Name)
            .Select(v => (Guid?)v.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<ReceiptDto> ToDtoAsync(Receipt receipt, CancellationToken cancellationToken)
    {
        var dto = receipt.MapTo<ReceiptDto>();
        await FillVendorNamesAsync(new List<ReceiptDto> { dto }, cancellationToken);
        return dto;
    }

    private async Task FillVendorNamesAsync(IList<ReceiptDto> items, CancellationToken cancellationToken)
    {
        var vendorIds = items.Where(r => r.MatchedVendorId != null).Select(r => r.MatchedVendorId!.Value).Distinct().ToList();
        if (vendorIds.Count == 0)
            return;

        var names = await _vendorRepository.AsNoTracking()
            .Where(v => vendorIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Name })
            .ToDictionaryAsync(v => v.Id, v => v.Name, cancellationToken);

        foreach (var dto in items)
            if (dto.MatchedVendorId != null)
                dto.MatchedVendorName = names.GetValueOrDefault(dto.MatchedVendorId.Value);
    }
}
