namespace Tnzi.Finance.Services;

/// <summary>
/// EFT 批次服务
/// </summary>
public class EftService : ApplicationService, IEftService
{
    private readonly IRepository<EftBatch, Guid> _batchRepository;
    private readonly IRepository<EftBatchLine, Guid> _lineRepository;
    private readonly IRepository<BankAccount, Guid> _bankAccountRepository;
    private readonly IReadOnlyRepository<PaymentEntry, Guid> _paymentRepository;
    private readonly IReadOnlyRepository<PartyBankAccount, Guid> _partyBankRepository;
    private readonly IReadOnlyRepository<Vendor, Guid> _vendorRepository;
    private readonly IDocumentNumberService _numberService;
    private readonly IEftFileComposer _composer;
    private readonly IFinanceDataProtector _protector;
    private readonly FinanceOptions _options;

    public EftService(
        IServiceProvider serviceProvider,
        IRepository<EftBatch, Guid> batchRepository,
        IRepository<EftBatchLine, Guid> lineRepository,
        IRepository<BankAccount, Guid> bankAccountRepository,
        IReadOnlyRepository<PaymentEntry, Guid> paymentRepository,
        IReadOnlyRepository<PartyBankAccount, Guid> partyBankRepository,
        IReadOnlyRepository<Vendor, Guid> vendorRepository,
        IDocumentNumberService numberService,
        IEftFileComposer composer,
        IFinanceDataProtector protector,
        IOptionsSnapshot<FinanceOptions> options)
        : base(serviceProvider)
    {
        _batchRepository = Check.NotNull(batchRepository);
        _lineRepository = Check.NotNull(lineRepository);
        _bankAccountRepository = Check.NotNull(bankAccountRepository);
        _paymentRepository = Check.NotNull(paymentRepository);
        _partyBankRepository = Check.NotNull(partyBankRepository);
        _vendorRepository = Check.NotNull(vendorRepository);
        _numberService = Check.NotNull(numberService);
        _composer = Check.NotNull(composer);
        _protector = Check.NotNull(protector);
        _options = Check.NotNull(options).Value;
    }

    private static string CurrencyForFormat(EftFileFormat format) => format == EftFileFormat.Nacha ? "USD" : "CAD";
    private static BankNumberScheme SchemeForFormat(EftFileFormat format) => format == EftFileFormat.Nacha ? BankNumberScheme.UsAba : BankNumberScheme.CaEft;

    public async Task<Result<List<EftQueueItemDto>>> GetQueueAsync(CancellationToken cancellationToken = default)
    {
        var payments = await _paymentRepository.AsNoTracking()
            .Where(p => p.Status == FinanceDocumentStatus.Posted
                && p.Direction == PaymentDirection.Outbound
                && p.PaymentMethod != null && p.PaymentMethod.ToLower() == "banktransfer")
            .OrderBy(p => p.Number)
            .ToListAsync(cancellationToken);
        if (payments.Count == 0)
            return Ok(new List<EftQueueItemDto>());

        var paymentIds = payments.Select(p => p.Id).ToList();
        var batched = (await _lineRepository.AsNoTracking()
            .Where(l => paymentIds.Contains(l.PaymentEntryId))
            .Select(l => l.PaymentEntryId)
            .ToListAsync(cancellationToken)).ToHashSet();

        var candidates = payments.Where(p => !batched.Contains(p.Id)).ToList();
        if (candidates.Count == 0)
            return Ok(new List<EftQueueItemDto>());

        var partyIds = candidates.Select(p => p.PartyId).Distinct().ToList();
        var defaults = await _partyBankRepository.AsNoTracking()
            .Where(a => a.IsDefault && a.IsActive && partyIds.Contains(a.PartyId))
            .ToListAsync(cancellationToken);
        var defaultByParty = defaults
            .GroupBy(a => (a.PartyType, a.PartyId))
            .ToDictionary(g => g.Key, g => g.First());

        var vendorNames = await LoadVendorNamesAsync(candidates.Select(p => p.PartyId), cancellationToken);

        var items = new List<EftQueueItemDto>();
        foreach (var p in candidates)
        {
            if (!defaultByParty.TryGetValue((p.PartyType, p.PartyId), out var bank))
                continue;
            items.Add(new EftQueueItemDto
            {
                PaymentEntryId = p.Id,
                PaymentNumber = p.Number,
                PartyType = p.PartyType,
                PartyId = p.PartyId,
                PayeeName = vendorNames.GetValueOrDefault(p.PartyId),
                DocDate = p.DocDate,
                Currency = p.Currency,
                Amount = p.Amount,
                PartyBankAccountId = bank.Id,
                PartyBankAccountMasked = bank.AccountNumberMasked,
                PartyScheme = bank.Scheme
            });
        }

        return Ok(items);
    }

    public async Task<Result<IPagedList<EftBatchDto>>> GetPagedAsync(EftBatchQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var queryable = _batchRepository.AsNoTracking();
        if (query.BankAccountId.HasValue)
            queryable = queryable.Where(b => b.BankAccountId == query.BankAccountId.Value);
        if (query.Status.HasValue)
            queryable = queryable.Where(b => b.Status == query.Status.Value);
        if (query.Format.HasValue)
            queryable = queryable.Where(b => b.Format == query.Format.Value);

        var pagedList = await queryable
            .OrderByDescending(b => b.CreationTime)
            .ProjectTo<EftBatch, EftBatchDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        await FillBankNamesAsync(pagedList.Items, cancellationToken);
        return Ok(pagedList);
    }

    public async Task<Result<EftBatchDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var batch = await _batchRepository.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (batch == null)
            return Fail<EftBatchDto>("EFT batch not found.", 404);

        return Ok(await ToDtoAsync(batch, cancellationToken));
    }

    public async Task<Result<EftBatchDto>> CreateBatchAsync(CreateEftBatchDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);
        if (input.PaymentEntryIds == null || input.PaymentEntryIds.Count == 0)
            return Fail<EftBatchDto>("Select at least one payment for the batch.", 400);

        var bank = await _bankAccountRepository.AsNoTracking().FirstOrDefaultAsync(b => b.Id == input.BankAccountId, cancellationToken);
        if (bank == null)
            return Fail<EftBatchDto>("Bank account not found.", 404);

        var scheme = SchemeForFormat(input.Format);
        if (bank.Scheme != scheme)
            return Fail<EftBatchDto>($"The {input.Format} format requires a {scheme} bank account.", 400);

        // The effective (settlement) date must not be in the past: an ODFI rejects
        // a file whose effective date has already passed. Banking-day/holiday checks
        // are out of scope (they need a per-region calendar); this catches the common
        // "dated last week" mistake up front instead of at bank submission.
        if (input.EffectiveDate.ToUtcDate() < DateTime.UtcNow.ToUtcDate())
            return Fail<EftBatchDto>("The effective date cannot be in the past.", 400);

        var currency = CurrencyForFormat(input.Format);

        var ids = input.PaymentEntryIds.Distinct().ToList();
        var payments = await _paymentRepository.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);
        if (payments.Count != ids.Count)
            return Fail<EftBatchDto>("One or more payments were not found.", 404);

        var partyIds = payments.Select(p => p.PartyId).Distinct().ToList();
        var defaults = await _partyBankRepository.AsNoTracking()
            .Where(a => a.IsDefault && a.IsActive && partyIds.Contains(a.PartyId))
            .ToListAsync(cancellationToken);
        var defaultByParty = defaults
            .GroupBy(a => (a.PartyType, a.PartyId))
            .ToDictionary(g => g.Key, g => g.First());

        var vendorNames = await LoadVendorNamesAsync(payments.Select(p => p.PartyId), cancellationToken);

        var lines = new List<EftBatchLine>();
        decimal total = 0m;
        foreach (var p in payments)
        {
            if (p.Status != FinanceDocumentStatus.Posted || p.Direction != PaymentDirection.Outbound)
                return Fail<EftBatchDto>($"Payment '{p.Number ?? p.Id.ToString()}' is not a posted outbound payment.", 400);
            if (string.IsNullOrWhiteSpace(p.PaymentMethod) || !string.Equals(p.PaymentMethod, PaymentMethods.BankTransfer, StringComparison.OrdinalIgnoreCase))
                return Fail<EftBatchDto>($"Payment '{p.Number ?? p.Id.ToString()}' is not a bank transfer.", 400);
            if (!string.Equals(p.Currency, currency, StringComparison.OrdinalIgnoreCase))
                return Fail<EftBatchDto>($"Payment '{p.Number ?? p.Id.ToString()}' currency {p.Currency} does not match the {input.Format} currency {currency}.", 400);

            if (!defaultByParty.TryGetValue((p.PartyType, p.PartyId), out var partyBank))
                return Fail<EftBatchDto>($"Payee for payment '{p.Number ?? p.Id.ToString()}' has no default bank account on file.", 400);
            if (partyBank.Scheme != scheme)
                return Fail<EftBatchDto>($"Payee bank account for payment '{p.Number ?? p.Id.ToString()}' does not match the {input.Format} scheme.", 400);
            if (string.IsNullOrWhiteSpace(partyBank.AccountNumberEncrypted))
                return Fail<EftBatchDto>($"Payee bank account for payment '{p.Number ?? p.Id.ToString()}' has no account number on file.", 400);

            lines.Add(new EftBatchLine
            {
                PaymentEntryId = p.Id,
                PartyBankAccountId = partyBank.Id,
                Amount = p.Amount,
                PayeeName = vendorNames.GetValueOrDefault(p.PartyId),
                TenantId = p.TenantId
            });
            total += p.Amount;
        }

        var batch = new EftBatch
        {
            BankAccountId = input.BankAccountId,
            Format = input.Format,
            Currency = currency,
            EffectiveDate = input.EffectiveDate.ToUtcDate(),
            Status = EftBatchStatus.Draft,
            TotalCount = lines.Count,
            TotalAmount = total
        };

        try
        {
            var result = await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                await _batchRepository.InsertAsync(batch, ct);
                foreach (var line in lines)
                {
                    line.EftBatchId = batch.Id;
                    await _lineRepository.InsertAsync(line, ct);
                }
                await _lineRepository.SaveChangesAsync(ct);
                return Result.Success();
            }, cancellationToken);
            if (!result.Succeeded)
                return Fail<EftBatchDto>(result.Message ?? "Failed to create batch.", result.Code ?? 400);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<EftBatchDto>("One or more payments are already in another batch. Reload and retry.", 409);
        }

        return await GetAsync(batch.Id, cancellationToken);
    }

    public async Task<Result<EftBatchDto>> GenerateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var batch = await _batchRepository.AsQueryable(true).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (batch == null)
            return Fail<EftBatchDto>("EFT batch not found.", 404);
        if (batch.Status != EftBatchStatus.Draft)
            return Fail<EftBatchDto>("Only a draft batch can be generated. Void and rebuild to change a generated batch.", 409);

        if (!_protector.IsConfigured)
            return Fail<EftBatchDto>("Configure Finance:Encryption:EncryptionKey before generating EFT files.", 400);

        var bank = await _bankAccountRepository.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batch.BankAccountId, cancellationToken);
        if (bank == null)
            return Fail<EftBatchDto>("Bank account not found.", 404);
        if (string.IsNullOrWhiteSpace(bank.EftOriginatorId) || string.IsNullOrWhiteSpace(bank.EftOriginatorName))
            return Fail<EftBatchDto>("Configure the bank account's EFT originator id and name before generating a file.", 400);

        var lines = await _lineRepository.AsNoTracking().Where(l => l.EftBatchId == batch.Id).ToListAsync(cancellationToken);
        if (lines.Count == 0)
            return Fail<EftBatchDto>("The batch has no lines to generate.", 400);

        var partyBankIds = lines.Select(l => l.PartyBankAccountId).Distinct().ToList();
        var partyBanks = await _partyBankRepository.AsNoTracking()
            .Where(a => partyBankIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, cancellationToken);

        // 明文账号仅在内存栈拼装文件
        var request = new EftComposeRequest
        {
            Format = batch.Format,
            Currency = batch.Currency,
            EffectiveDate = batch.EffectiveDate,
            CreationTime = DateTime.UtcNow,
            OriginatorId = bank.EftOriginatorId,
            OriginatorName = bank.EftOriginatorName,
            BankName = bank.BankName,
            OriginatorRoutingNumber = bank.RoutingNumber,
            OriginatorInstitutionNumber = bank.InstitutionNumber,
            OriginatorTransitNumber = bank.TransitNumber,
            OriginatorAccountNumber = DecryptOrEmpty(bank.AccountNumberEncrypted, FinanceProtectionAad.ForBankAccount(bank.AccountId))
        };

        foreach (var line in lines)
        {
            if (!partyBanks.TryGetValue(line.PartyBankAccountId, out var partyBank))
                return Fail<EftBatchDto>("A payee bank account referenced by the batch no longer exists.", 400);
            request.Entries.Add(new EftComposeEntry
            {
                PayeeName = line.PayeeName ?? string.Empty,
                RoutingNumber = partyBank.RoutingNumber,
                InstitutionNumber = partyBank.InstitutionNumber,
                TransitNumber = partyBank.TransitNumber,
                AccountNumber = DecryptOrEmpty(partyBank.AccountNumberEncrypted, FinanceProtectionAad.ForPartyBankAccount(partyBank.PartyType, partyBank.PartyId)),
                AccountType = partyBank.AccountType,
                Amount = line.Amount,
                Reference = null
            });
        }

        var total = lines.Sum(l => l.Amount);

        try
        {
            var result = await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                var number = await _numberService.NextFormattedAsync(nameof(EftBatch), _options.EftNumberPrefix, _options.JournalNumberPadding, ct);
                var fileCreationNumber = await AllocateFileCreationNumberAsync(bank.Id, ct);
                request.FileCreationNumber = fileCreationNumber;

                var composed = _composer.Compose(request);
                if (!composed.Succeeded)
                    throw new UnitOfWorkAbortException(Result.Failure(composed.Message ?? "EFT composition failed.", composed.Code ?? 400));

                batch.Number = number;
                batch.FileCreationNumber = fileCreationNumber;
                batch.Status = EftBatchStatus.Generated;
                batch.GeneratedTime = DateTime.UtcNow;
                batch.TotalCount = lines.Count;
                batch.TotalAmount = total;
                batch.FileName = $"{number}.{composed.Data!.FileExtension}";
                batch.FileContentEncrypted = _protector.Protect(composed.Data.Content);
                await _batchRepository.UpdateAsync(batch, ct);
                return Result.Success();
            }, cancellationToken);
            if (!result.Succeeded)
                return Fail<EftBatchDto>(result.Message ?? "Failed to generate the file.", result.Code ?? 400);
        }
        catch (UnitOfWorkAbortException ex)
        {
            return Fail<EftBatchDto>(ex.Result.Message ?? "Failed to generate the file.", ex.Result.Code ?? 400);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<EftBatchDto>("The batch was modified by another operation. Reload and retry.", 409);
        }

        await PublishEventAsync(new EftBatchGeneratedEvent
        {
            BatchId = batch.Id,
            Number = batch.Number,
            BankAccountId = batch.BankAccountId,
            Format = batch.Format,
            TotalCount = batch.TotalCount,
            TotalAmount = batch.TotalAmount,
            TenantId = batch.TenantId
        }, cancellationToken);

        return await GetAsync(batch.Id, cancellationToken);
    }

    public async Task<Result<EftFileDto>> DownloadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var batch = await _batchRepository.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (batch == null)
            return Fail<EftFileDto>("EFT batch not found.", 404);
        if (batch.Status != EftBatchStatus.Generated || string.IsNullOrEmpty(batch.FileContentEncrypted))
            return Fail<EftFileDto>("The batch has no generated file. Generate it first.", 400);
        if (!_protector.IsConfigured)
            return Fail<EftFileDto>("Configure Finance:Encryption:EncryptionKey to download EFT files.", 400);

        string content;
        try
        {
            content = _protector.Unprotect(batch.FileContentEncrypted!);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to decrypt EFT file for batch {BatchId}.", batch.Id);
            return Fail<EftFileDto>("The EFT file could not be decrypted.", 500);
        }

        return Ok(new EftFileDto
        {
            FileName = batch.FileName ?? $"{batch.Number ?? batch.Id.ToString()}.txt",
            Content = Encoding.UTF8.GetBytes(content)
        });
    }

    public async Task<Result<EftBatchDto>> VoidBatchAsync(Guid id, VoidEftBatchDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var batch = await _batchRepository.AsQueryable(true).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (batch == null)
            return Fail<EftBatchDto>("EFT batch not found.", 404);
        if (batch.Status == EftBatchStatus.Voided)
            return Fail<EftBatchDto>("The batch is already voided.", 409);

        try
        {
            await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                var lines = await _lineRepository.ToListAsync(l => l.EftBatchId == batch.Id, ct);
                if (lines.Count > 0)
                    await _lineRepository.DeleteManyAsync(lines, ct);

                batch.Status = EftBatchStatus.Voided;
                batch.VoidReason = input.Reason;
                await _batchRepository.UpdateAsync(batch, ct);
                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<EftBatchDto>("The batch was modified by another operation. Reload and retry.", 409);
        }

        return await GetAsync(batch.Id, cancellationToken);
    }

    /// <summary>原子递增 <see cref="BankAccount.EftFileCreationNumber"/> 并返回 1-9999 循环序号（须在活动事务内）。</summary>
    private async Task<int> AllocateFileCreationNumberAsync(Guid bankAccountId, CancellationToken cancellationToken)
    {
        await _bankAccountRepository.EnsureTransactionStartedAsync(cancellationToken);
        await _bankAccountRepository.AsQueryable(true)
            .Where(b => b.Id == bankAccountId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EftFileCreationNumber, x => x.EftFileCreationNumber + 1), cancellationToken);
        var raw = await _bankAccountRepository.AsQueryable()
            .Where(b => b.Id == bankAccountId)
            .Select(b => b.EftFileCreationNumber)
            .FirstAsync(cancellationToken);
        return ((raw - 1) % 9999) + 1;
    }

    private string DecryptOrEmpty(string? encrypted, string associatedData)
        => string.IsNullOrEmpty(encrypted) ? string.Empty : _protector.Unprotect(encrypted, associatedData);

    private async Task<Dictionary<Guid, string>> LoadVendorNamesAsync(IEnumerable<Guid> partyIds, CancellationToken cancellationToken)
    {
        var ids = partyIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, string>();

        return await _vendorRepository.AsNoTracking()
            .Where(v => ids.Contains(v.Id))
            .Select(v => new { v.Id, v.Name })
            .ToDictionaryAsync(v => v.Id, v => v.Name, cancellationToken);
    }

    private async Task<EftBatchDto> ToDtoAsync(EftBatch batch, CancellationToken cancellationToken)
    {
        var dto = batch.MapTo<EftBatchDto>();
        await FillBankNamesAsync(new List<EftBatchDto> { dto }, cancellationToken);

        var lines = await _lineRepository.AsNoTracking().Where(l => l.EftBatchId == batch.Id).ToListAsync(cancellationToken);
        if (lines.Count > 0)
        {
            var paymentIds = lines.Select(l => l.PaymentEntryId).Distinct().ToList();
            var paymentNumbers = await _paymentRepository.AsNoTracking()
                .Where(p => paymentIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Number })
                .ToDictionaryAsync(p => p.Id, p => p.Number, cancellationToken);

            var partyBankIds = lines.Select(l => l.PartyBankAccountId).Distinct().ToList();
            var masks = await _partyBankRepository.AsNoTracking()
                .Where(a => partyBankIds.Contains(a.Id))
                .Select(a => new { a.Id, a.AccountNumberMasked })
                .ToDictionaryAsync(a => a.Id, a => a.AccountNumberMasked, cancellationToken);

            dto.Lines = lines.Select(l => new EftBatchLineDto
            {
                Id = l.Id,
                PaymentEntryId = l.PaymentEntryId,
                PaymentNumber = paymentNumbers.GetValueOrDefault(l.PaymentEntryId),
                PartyBankAccountId = l.PartyBankAccountId,
                PartyBankAccountMasked = masks.GetValueOrDefault(l.PartyBankAccountId),
                Amount = l.Amount,
                PayeeName = l.PayeeName
            }).ToList();
        }

        return dto;
    }

    private async Task FillBankNamesAsync(IList<EftBatchDto> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            return;

        var bankIds = items.Select(b => b.BankAccountId).Distinct().ToList();
        var names = await _bankAccountRepository.AsNoTracking()
            .Where(b => bankIds.Contains(b.Id))
            .Select(b => new { b.Id, b.Name })
            .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);

        foreach (var dto in items)
            dto.BankAccountName = names.GetValueOrDefault(dto.BankAccountId);
    }
}
