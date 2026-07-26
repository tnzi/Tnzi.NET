namespace Tnzi.Finance.Services;

/// <summary>
/// 报价单服务
/// </summary>
/// <remarks>
/// **本服务从不触碰总账**：没有过账、没有凭证、没有捕获汇率。它管的是一次商业
/// 承诺的流转，会计事实要等到转换出来的发票被过账时才产生。
///
/// 编号在 <see cref="SendAsync"/> 分配（草稿不占号），且是该方法里最后一个可失败
/// 步骤之后——与全模块的连续编号铁律一致，校验失败不会烧号留下缺口。
/// </remarks>
public class EstimateService : ApplicationService, IEstimateService
{
    private readonly IRepository<Estimate, Guid> _repository;
    private readonly IRepository<EstimateLine, Guid> _lineRepository;
    private readonly IReadOnlyRepository<Customer, Guid> _customerRepository;
    private readonly IDocumentNumberService _numberService;
    private readonly IInvoiceService _invoiceService;
    private readonly IReadOnlyRepository<Invoice, Guid> _invoiceRepository;
    private readonly OfferComposer _composer;
    private readonly FinanceDocumentHelper _helper;
    private readonly FinanceOptions _options;

    public EstimateService(
        IServiceProvider serviceProvider,
        IRepository<Estimate, Guid> repository,
        IRepository<EstimateLine, Guid> lineRepository,
        IReadOnlyRepository<Customer, Guid> customerRepository,
        IDocumentNumberService numberService,
        IInvoiceService invoiceService,
        IReadOnlyRepository<Invoice, Guid> invoiceRepository,
        OfferComposer composer,
        FinanceDocumentHelper helper,
        IOptionsSnapshot<FinanceOptions> options)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _lineRepository = Check.NotNull(lineRepository);
        _customerRepository = Check.NotNull(customerRepository);
        _numberService = Check.NotNull(numberService);
        _invoiceService = Check.NotNull(invoiceService);
        _invoiceRepository = Check.NotNull(invoiceRepository);
        _composer = Check.NotNull(composer);
        _helper = Check.NotNull(helper);
        _options = Check.NotNull(options).Value;
    }

    public async Task<Result<IPagedList<EstimateDto>>> GetPagedAsync(EstimateQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var pagedList = await _repository.AsNoTracking()
            .Filter(query)
            .OrderByDescending(e => e.CreationTime)
            .Select(e => new EstimateDto
            {
                Id = e.Id,
                Number = e.Number,
                Status = e.Status,
                CustomerId = e.CustomerId,
                CustomerName = e.Customer!.Name,
                DocDate = e.DocDate,
                ExpiryDate = e.ExpiryDate,
                Currency = e.Currency,
                SubTotal = e.SubTotal,
                TaxTotal = e.TaxTotal,
                Total = e.Total,
                Memo = e.Memo,
                InternalNote = e.InternalNote,
                ConvertedToDocType = e.ConvertedToDocType,
                ConvertedToDocId = e.ConvertedToDocId,
                CreationTime = e.CreationTime
            })
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        return Ok(pagedList);
    }

    public async Task<Result<EstimateDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var estimate = await _repository.AsNoTracking()
            .Include(e => e.Lines)
            .Include(e => e.Customer)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        if (estimate == null)
            return Fail<EstimateDto>("Estimate not found.", 404);

        return Ok(ToDto(estimate));
    }

    public async Task<Result<EstimateDto>> CreateDraftAsync(CreateEstimateDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var estimate = new Estimate();
        var applyResult = await ApplyAsync(estimate, input, cancellationToken);
        if (!applyResult.Succeeded)
            return Fail<EstimateDto>(applyResult.Message ?? "Invalid estimate.", applyResult.Code ?? 400);

        await _repository.InsertAsync(estimate, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return await GetAsync(estimate.Id, cancellationToken);
    }

    public async Task<Result<EstimateDto>> UpdateAsync(Guid id, CreateEstimateDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var estimate = await _repository.AsQueryable(true)
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        if (estimate == null)
            return Fail<EstimateDto>("Estimate not found.", 404);
        if (!OfferComposer.CanEdit(estimate.Status))
            return Fail<EstimateDto>($"A {estimate.Status.ToString().ToLowerInvariant()} estimate can no longer be edited.", 409);

        var oldLines = estimate.Lines.ToList();
        var applyResult = await ApplyAsync(estimate, input, cancellationToken);
        if (!applyResult.Succeeded)
            return Fail<EstimateDto>(applyResult.Message ?? "Invalid estimate.", applyResult.Code ?? 400);

        try
        {
            await ExecuteInUnitOfWorkAsync(async ct =>
            {
                if (oldLines.Count > 0)
                    await _lineRepository.DeleteManyAsync(oldLines, ct);
                await _repository.UpdateAsync(estimate, ct);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<EstimateDto>("The estimate was modified by another operation. Reload and retry.", 409);
        }

        return await GetAsync(estimate.Id, cancellationToken);
    }

    public async Task<Result> DeleteDraftAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var estimate = await _repository.AsQueryable(true)
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        if (estimate == null)
            return Fail("Estimate not found.", 404);

        // 已发出的报价单对方手里有一份；删掉它等于让号段出现一个谁也解释不了的缺口。
        if (estimate.Status != FinanceOfferStatus.Draft)
            return Fail("Only draft estimates can be deleted. A sent estimate can be declined or closed.", 409);

        try
        {
            await ExecuteInUnitOfWorkAsync(async ct =>
            {
                if (estimate.Lines.Count > 0)
                    await _lineRepository.DeleteManyAsync(estimate.Lines.ToList(), ct);
                await _repository.DeleteAsync(estimate, ct);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail("The estimate was modified by another operation. Reload and retry.", 409);
        }

        return Ok();
    }

    public async Task<Result<EstimateDto>> SendAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var estimate = await _repository.AsQueryable(true).FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (estimate == null)
            return Fail<EstimateDto>("Estimate not found.", 404);
        if (!OfferComposer.CanSend(estimate.Status))
            return Fail<EstimateDto>($"A {estimate.Status.ToString().ToLowerInvariant()} estimate cannot be sent.", 409);

        try
        {
            await ExecuteInUnitOfWorkAsync(async ct =>
            {
                // 重新报价（Declined → Sent）保留原号：对方引用的就是那个号。
                estimate.Number ??= await _numberService.NextFormattedAsync(
                    FinanceOfferScopes.Estimate, _options.EstimateNumberPrefix, _options.JournalNumberPadding, ct);
                estimate.Status = FinanceOfferStatus.Sent;
                await _repository.UpdateAsync(estimate, ct);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<EstimateDto>("The estimate was modified by another operation. Reload and retry.", 409);
        }

        await PublishEventAsync(new FinanceOfferSentEvent
        {
            DocType = FinanceOfferScopes.Estimate,
            DocId = estimate.Id,
            Number = estimate.Number!,
            PartyId = estimate.CustomerId,
            Total = estimate.Total,
            Currency = estimate.Currency,
            TenantId = estimate.TenantId
        }, cancellationToken);

        return await GetAsync(estimate.Id, cancellationToken);
    }

    public Task<Result<EstimateDto>> AcceptAsync(Guid id, CancellationToken cancellationToken = default)
        => TransitionAsync(id, OfferComposer.CanAccept, FinanceOfferStatus.Accepted, "accepted", cancellationToken);

    public Task<Result<EstimateDto>> DeclineAsync(Guid id, CancellationToken cancellationToken = default)
        => TransitionAsync(id, OfferComposer.CanDecline, FinanceOfferStatus.Declined, "declined", cancellationToken);

    public Task<Result<EstimateDto>> CloseAsync(Guid id, CancellationToken cancellationToken = default)
        => TransitionAsync(id, OfferComposer.CanClose, FinanceOfferStatus.Closed, "closed", cancellationToken);

    public async Task<Result<ConvertOfferResultDto>> ConvertToInvoiceAsync(Guid id, ConvertOfferDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var estimate = await _repository.AsQueryable(true)
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        if (estimate == null)
            return Fail<ConvertOfferResultDto>("Estimate not found.", 404);

        if (estimate.Status == FinanceOfferStatus.Converted)
        {
            // Self-healing: if the draft this became was since deleted, the
            // estimate is convertible again. Without this it would be frozen
            // as "converted" pointing at nothing - a dead end reachable by the
            // ordinary "I converted by mistake, let me delete the draft" move.
            // The check lives here rather than as a delete guard on the invoice
            // side so the dependency keeps pointing one way: offers know about
            // invoices, invoices know nothing about offers.
            var targetStillExists = estimate.ConvertedToDocId.HasValue
                && await _invoiceRepository.AnyAsync(i => i.Id == estimate.ConvertedToDocId.Value, cancellationToken);
            if (targetStillExists)
                return Fail<ConvertOfferResultDto>("This estimate has already been converted.", 409);
        }
        else if (!OfferComposer.CanConvert(estimate.Status))
        {
            return Fail<ConvertOfferResultDto>("Only a sent or accepted estimate can be converted to an invoice.", 409);
        }

        var draft = new CreateInvoiceDto
        {
            CustomerId = estimate.CustomerId,
            DocDate = (input.DocDate ?? DateTime.UtcNow).ToUtcDate(),
            DueDate = input.DueDate?.ToUtcDate(),
            Currency = estimate.Currency,
            Memo = estimate.Memo,
            Lines = estimate.Lines.OrderBy(l => l.LineNumber).Select(l => new CreateInvoiceLineDto
            {
                ItemId = l.ItemId,
                Description = l.Description,
                AccountId = l.AccountId,
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                TaxCodeId = l.TaxCodeId
            }).ToList()
        };

        Guid invoiceId;
        try
        {
            // 一个工作单元：发票草稿与"已转换"标记要么一起成立，要么都不成立。
            // 中途失败必须 abort 而不是 return（本模块铁律：失败 Result 仍会提交）。
            invoiceId = await ExecuteInUnitOfWorkAsync(async ct =>
            {
                var created = await _invoiceService.CreateDraftAsync(draft, ct);
                if (!created.Succeeded)
                    throw new UnitOfWorkAbortException(Result.Failure(created.Message ?? "Could not create the invoice draft.", created.Code ?? 400));

                estimate.Status = FinanceOfferStatus.Converted;
                estimate.ConvertedToDocType = FinanceSourceTypes.Invoice;
                estimate.ConvertedToDocId = created.Data!.Id;
                await _repository.UpdateAsync(estimate, ct);

                return created.Data.Id;
            }, cancellationToken);
        }
        catch (UnitOfWorkAbortException ex)
        {
            return Fail<ConvertOfferResultDto>(ex.Result.Message ?? "Conversion failed.", ex.Result.Code ?? 400);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<ConvertOfferResultDto>("The estimate was modified by another operation. Reload and retry.", 409);
        }

        await PublishEventAsync(new FinanceOfferConvertedEvent
        {
            SourceType = FinanceOfferScopes.Estimate,
            SourceId = estimate.Id,
            SourceNumber = estimate.Number,
            TargetType = FinanceSourceTypes.Invoice,
            TargetId = invoiceId,
            TenantId = estimate.TenantId
        }, cancellationToken);

        return Ok(new ConvertOfferResultDto
        {
            SourceId = estimate.Id,
            SourceNumber = estimate.Number,
            DocType = FinanceSourceTypes.Invoice,
            DocId = invoiceId
        });
    }

    private async Task<Result<EstimateDto>> TransitionAsync(
        Guid id, Func<FinanceOfferStatus, bool> allowed, FinanceOfferStatus target, string verb, CancellationToken cancellationToken)
    {
        var estimate = await _repository.AsQueryable(true).FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (estimate == null)
            return Fail<EstimateDto>("Estimate not found.", 404);
        if (!allowed(estimate.Status))
            return Fail<EstimateDto>($"A {estimate.Status.ToString().ToLowerInvariant()} estimate cannot be {verb}.", 409);

        try
        {
            estimate.Status = target;
            await _repository.UpdateAsync(estimate, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<EstimateDto>("The estimate was modified by another operation. Reload and retry.", 409);
        }

        return await GetAsync(estimate.Id, cancellationToken);
    }

    private async Task<Result> ApplyAsync(Estimate estimate, CreateEstimateDto input, CancellationToken cancellationToken)
    {
        if (!await _customerRepository.AnyAsync(c => c.Id == input.CustomerId, cancellationToken))
            return Fail("Customer not found.", 404);

        var composition = await _composer.ComposeAsync(input.Lines, isPurchase: false, cancellationToken);
        if (!composition.Succeeded)
            return Fail(composition.Message ?? "Invalid lines.", composition.Code ?? 400);

        var docDate = input.DocDate.ToUtcDate();
        var expiry = input.ExpiryDate?.ToUtcDate();
        if (expiry.HasValue && expiry.Value < docDate)
            return Fail("The expiry date cannot precede the estimate date.", 400);

        estimate.CustomerId = input.CustomerId;
        estimate.DocDate = docDate;
        estimate.ExpiryDate = expiry;
        estimate.Currency = _helper.NormalizeCurrency(input.Currency);
        estimate.Memo = input.Memo;
        estimate.InternalNote = input.InternalNote;

        estimate.Lines.Clear();
        foreach (var line in composition.Data!.Lines)
        {
            estimate.Lines.Add(new EstimateLine
            {
                EstimateId = estimate.Id,
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

        estimate.SubTotal = composition.Data.SubTotal;
        estimate.TaxTotal = composition.Data.TaxTotal;
        estimate.Total = composition.Data.Total;
        return Ok();
    }

    private static EstimateDto ToDto(Estimate estimate)
    {
        var dto = estimate.MapTo<EstimateDto>();
        dto.CustomerName = estimate.Customer?.Name;
        dto.Lines = estimate.Lines.OrderBy(l => l.LineNumber).Select(l => l.MapTo<OfferLineDto>()).ToList();
        return dto;
    }
}
