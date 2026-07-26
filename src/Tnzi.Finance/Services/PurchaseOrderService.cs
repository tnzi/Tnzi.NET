namespace Tnzi.Finance.Services;

/// <summary>
/// 采购订单服务
/// </summary>
/// <remarks>
/// <see cref="EstimateService"/> 的镜像：往来方换成供应商、税走进项口径、转换目标
/// 是账单。同样**从不触碰总账**——下单不是费用，会计事实要等到转换出来的账单被
/// 过账时才产生。
/// </remarks>
public class PurchaseOrderService : ApplicationService, IPurchaseOrderService
{
    private readonly IRepository<PurchaseOrder, Guid> _repository;
    private readonly IRepository<PurchaseOrderLine, Guid> _lineRepository;
    private readonly IReadOnlyRepository<Vendor, Guid> _vendorRepository;
    private readonly IDocumentNumberService _numberService;
    private readonly IBillService _billService;
    private readonly IReadOnlyRepository<Bill, Guid> _billRepository;
    private readonly OfferComposer _composer;
    private readonly FinanceDocumentHelper _helper;
    private readonly FinanceOptions _options;

    public PurchaseOrderService(
        IServiceProvider serviceProvider,
        IRepository<PurchaseOrder, Guid> repository,
        IRepository<PurchaseOrderLine, Guid> lineRepository,
        IReadOnlyRepository<Vendor, Guid> vendorRepository,
        IDocumentNumberService numberService,
        IBillService billService,
        IReadOnlyRepository<Bill, Guid> billRepository,
        OfferComposer composer,
        FinanceDocumentHelper helper,
        IOptionsSnapshot<FinanceOptions> options)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _lineRepository = Check.NotNull(lineRepository);
        _vendorRepository = Check.NotNull(vendorRepository);
        _numberService = Check.NotNull(numberService);
        _billService = Check.NotNull(billService);
        _billRepository = Check.NotNull(billRepository);
        _composer = Check.NotNull(composer);
        _helper = Check.NotNull(helper);
        _options = Check.NotNull(options).Value;
    }

    public async Task<Result<IPagedList<PurchaseOrderDto>>> GetPagedAsync(PurchaseOrderQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var pagedList = await _repository.AsNoTracking()
            .Filter(query)
            .OrderByDescending(o => o.CreationTime)
            .Select(o => new PurchaseOrderDto
            {
                Id = o.Id,
                Number = o.Number,
                Status = o.Status,
                VendorId = o.VendorId,
                VendorName = o.Vendor!.Name,
                DocDate = o.DocDate,
                ExpectedDate = o.ExpectedDate,
                Currency = o.Currency,
                SubTotal = o.SubTotal,
                TaxTotal = o.TaxTotal,
                Total = o.Total,
                Memo = o.Memo,
                InternalNote = o.InternalNote,
                ShipTo = o.ShipTo,
                ConvertedToDocType = o.ConvertedToDocType,
                ConvertedToDocId = o.ConvertedToDocId,
                CreationTime = o.CreationTime
            })
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        return Ok(pagedList);
    }

    public async Task<Result<PurchaseOrderDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await _repository.AsNoTracking()
            .Include(o => o.Lines)
            .Include(o => o.Vendor)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        if (order == null)
            return Fail<PurchaseOrderDto>("Purchase order not found.", 404);

        return Ok(ToDto(order));
    }

    public async Task<Result<PurchaseOrderDto>> CreateDraftAsync(CreatePurchaseOrderDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var order = new PurchaseOrder();
        var applyResult = await ApplyAsync(order, input, cancellationToken);
        if (!applyResult.Succeeded)
            return Fail<PurchaseOrderDto>(applyResult.Message ?? "Invalid purchase order.", applyResult.Code ?? 400);

        await _repository.InsertAsync(order, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return await GetAsync(order.Id, cancellationToken);
    }

    public async Task<Result<PurchaseOrderDto>> UpdateAsync(Guid id, CreatePurchaseOrderDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var order = await _repository.AsQueryable(true)
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        if (order == null)
            return Fail<PurchaseOrderDto>("Purchase order not found.", 404);
        if (!OfferComposer.CanEdit(order.Status))
            return Fail<PurchaseOrderDto>($"A {order.Status.ToString().ToLowerInvariant()} purchase order can no longer be edited.", 409);

        var oldLines = order.Lines.ToList();
        var applyResult = await ApplyAsync(order, input, cancellationToken);
        if (!applyResult.Succeeded)
            return Fail<PurchaseOrderDto>(applyResult.Message ?? "Invalid purchase order.", applyResult.Code ?? 400);

        try
        {
            await ExecuteInUnitOfWorkAsync(async ct =>
            {
                if (oldLines.Count > 0)
                    await _lineRepository.DeleteManyAsync(oldLines, ct);
                await _repository.UpdateAsync(order, ct);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<PurchaseOrderDto>("The purchase order was modified by another operation. Reload and retry.", 409);
        }

        return await GetAsync(order.Id, cancellationToken);
    }

    public async Task<Result> DeleteDraftAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await _repository.AsQueryable(true)
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        if (order == null)
            return Fail("Purchase order not found.", 404);

        // 已发出的订单供应商手里有一份；删掉它等于让号段出现一个谁也解释不了的缺口。
        if (order.Status != FinanceOfferStatus.Draft)
            return Fail("Only draft purchase orders can be deleted. A sent order can be declined or closed.", 409);

        try
        {
            await ExecuteInUnitOfWorkAsync(async ct =>
            {
                if (order.Lines.Count > 0)
                    await _lineRepository.DeleteManyAsync(order.Lines.ToList(), ct);
                await _repository.DeleteAsync(order, ct);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail("The purchase order was modified by another operation. Reload and retry.", 409);
        }

        return Ok();
    }

    public async Task<Result<PurchaseOrderDto>> SendAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await _repository.AsQueryable(true).FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order == null)
            return Fail<PurchaseOrderDto>("Purchase order not found.", 404);
        if (!OfferComposer.CanSend(order.Status))
            return Fail<PurchaseOrderDto>($"A {order.Status.ToString().ToLowerInvariant()} purchase order cannot be sent.", 409);

        try
        {
            await ExecuteInUnitOfWorkAsync(async ct =>
            {
                // 重发保留原号：供应商引用的就是那个号。
                order.Number ??= await _numberService.NextFormattedAsync(
                    FinanceOfferScopes.PurchaseOrder, _options.PurchaseOrderNumberPrefix, _options.JournalNumberPadding, ct);
                order.Status = FinanceOfferStatus.Sent;
                await _repository.UpdateAsync(order, ct);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<PurchaseOrderDto>("The purchase order was modified by another operation. Reload and retry.", 409);
        }

        await PublishEventAsync(new FinanceOfferSentEvent
        {
            DocType = FinanceOfferScopes.PurchaseOrder,
            DocId = order.Id,
            Number = order.Number!,
            PartyId = order.VendorId,
            Total = order.Total,
            Currency = order.Currency,
            TenantId = order.TenantId
        }, cancellationToken);

        return await GetAsync(order.Id, cancellationToken);
    }

    public Task<Result<PurchaseOrderDto>> AcceptAsync(Guid id, CancellationToken cancellationToken = default)
        => TransitionAsync(id, OfferComposer.CanAccept, FinanceOfferStatus.Accepted, "accepted", cancellationToken);

    public Task<Result<PurchaseOrderDto>> DeclineAsync(Guid id, CancellationToken cancellationToken = default)
        => TransitionAsync(id, OfferComposer.CanDecline, FinanceOfferStatus.Declined, "declined", cancellationToken);

    public Task<Result<PurchaseOrderDto>> CloseAsync(Guid id, CancellationToken cancellationToken = default)
        => TransitionAsync(id, OfferComposer.CanClose, FinanceOfferStatus.Closed, "closed", cancellationToken);

    public async Task<Result<ConvertOfferResultDto>> ConvertToBillAsync(Guid id, ConvertOfferDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var order = await _repository.AsQueryable(true)
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        if (order == null)
            return Fail<ConvertOfferResultDto>("Purchase order not found.", 404);

        if (order.Status == FinanceOfferStatus.Converted)
        {
            // Self-healing when the draft this became was since deleted - see
            // the note in EstimateService.ConvertToInvoiceAsync.
            var targetStillExists = order.ConvertedToDocId.HasValue
                && await _billRepository.AnyAsync(b => b.Id == order.ConvertedToDocId.Value, cancellationToken);
            if (targetStillExists)
                return Fail<ConvertOfferResultDto>("This purchase order has already been converted.", 409);
        }
        else if (!OfferComposer.CanConvert(order.Status))
        {
            return Fail<ConvertOfferResultDto>("Only a sent or accepted purchase order can be converted to a bill.", 409);
        }

        var draft = new CreateBillDto
        {
            VendorId = order.VendorId,
            DocDate = (input.DocDate ?? DateTime.UtcNow).ToUtcDate(),
            DueDate = input.DueDate?.ToUtcDate(),
            Currency = order.Currency,
            Memo = order.Memo,
            Lines = order.Lines.OrderBy(l => l.LineNumber).Select(l => new CreateBillLineDto
            {
                ItemId = l.ItemId,
                Description = l.Description,
                AccountId = l.AccountId,
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                TaxCodeId = l.TaxCodeId
            }).ToList()
        };

        Guid billId;
        try
        {
            // 一个工作单元：账单草稿与"已转换"标记要么一起成立，要么都不成立。
            billId = await ExecuteInUnitOfWorkAsync(async ct =>
            {
                var created = await _billService.CreateDraftAsync(draft, ct);
                if (!created.Succeeded)
                    throw new UnitOfWorkAbortException(Result.Failure(created.Message ?? "Could not create the bill draft.", created.Code ?? 400));

                order.Status = FinanceOfferStatus.Converted;
                order.ConvertedToDocType = FinanceSourceTypes.Bill;
                order.ConvertedToDocId = created.Data!.Id;
                await _repository.UpdateAsync(order, ct);

                return created.Data.Id;
            }, cancellationToken);
        }
        catch (UnitOfWorkAbortException ex)
        {
            return Fail<ConvertOfferResultDto>(ex.Result.Message ?? "Conversion failed.", ex.Result.Code ?? 400);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<ConvertOfferResultDto>("The purchase order was modified by another operation. Reload and retry.", 409);
        }

        await PublishEventAsync(new FinanceOfferConvertedEvent
        {
            SourceType = FinanceOfferScopes.PurchaseOrder,
            SourceId = order.Id,
            SourceNumber = order.Number,
            TargetType = FinanceSourceTypes.Bill,
            TargetId = billId,
            TenantId = order.TenantId
        }, cancellationToken);

        return Ok(new ConvertOfferResultDto
        {
            SourceId = order.Id,
            SourceNumber = order.Number,
            DocType = FinanceSourceTypes.Bill,
            DocId = billId
        });
    }

    private async Task<Result<PurchaseOrderDto>> TransitionAsync(
        Guid id, Func<FinanceOfferStatus, bool> allowed, FinanceOfferStatus target, string verb, CancellationToken cancellationToken)
    {
        var order = await _repository.AsQueryable(true).FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order == null)
            return Fail<PurchaseOrderDto>("Purchase order not found.", 404);
        if (!allowed(order.Status))
            return Fail<PurchaseOrderDto>($"A {order.Status.ToString().ToLowerInvariant()} purchase order cannot be {verb}.", 409);

        try
        {
            order.Status = target;
            await _repository.UpdateAsync(order, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<PurchaseOrderDto>("The purchase order was modified by another operation. Reload and retry.", 409);
        }

        return await GetAsync(order.Id, cancellationToken);
    }

    private async Task<Result> ApplyAsync(PurchaseOrder order, CreatePurchaseOrderDto input, CancellationToken cancellationToken)
    {
        if (!await _vendorRepository.AnyAsync(v => v.Id == input.VendorId, cancellationToken))
            return Fail("Vendor not found.", 404);

        var composition = await _composer.ComposeAsync(input.Lines, isPurchase: true, cancellationToken);
        if (!composition.Succeeded)
            return Fail(composition.Message ?? "Invalid lines.", composition.Code ?? 400);

        var docDate = input.DocDate.ToUtcDate();
        var expected = input.ExpectedDate?.ToUtcDate();
        if (expected.HasValue && expected.Value < docDate)
            return Fail("The expected date cannot precede the order date.", 400);

        order.VendorId = input.VendorId;
        order.DocDate = docDate;
        order.ExpectedDate = expected;
        order.Currency = _helper.NormalizeCurrency(input.Currency);
        order.Memo = input.Memo;
        order.InternalNote = input.InternalNote;
        order.ShipTo = input.ShipTo;

        order.Lines.Clear();
        foreach (var line in composition.Data!.Lines)
        {
            order.Lines.Add(new PurchaseOrderLine
            {
                PurchaseOrderId = order.Id,
                LineNumber = line.LineNumber,
                ItemId = line.ItemId,
                Description = line.Description,
                AccountId = line.AccountId,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                Amount = line.Amount,
                TaxCodeId = line.TaxCodeId
            });
        }

        order.SubTotal = composition.Data.SubTotal;
        order.TaxTotal = composition.Data.TaxTotal;
        order.Total = composition.Data.Total;
        return Ok();
    }

    private static PurchaseOrderDto ToDto(PurchaseOrder order)
    {
        var dto = order.MapTo<PurchaseOrderDto>();
        dto.VendorName = order.Vendor?.Name;
        dto.Lines = order.Lines.OrderBy(l => l.LineNumber).Select(l => l.MapTo<OfferLineDto>()).ToList();
        return dto;
    }
}
