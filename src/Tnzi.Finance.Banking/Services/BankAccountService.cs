namespace Tnzi.Finance.Banking.Services;

/// <summary>
/// 银行账户档案服务
/// </summary>
/// <remarks>
/// 账号明文单向入库：写入即经 <see cref="IFinanceDataProtector"/> 加密到
/// <c>AccountNumberEncrypted</c> 并留 <c>AccountNumberMasked</c>（尾 4 位），DTO 永不回明文。
/// <c>AccountId</c> 唯一（每个资金科目至多一个档案），check-then-act 竞态由唯一过滤索引兜底。
/// </remarks>
public class BankAccountService : ApplicationService, IBankAccountService
{
    private readonly IRepository<BankAccount, Guid> _bankAccountRepository;
    private readonly IReadOnlyRepository<Account, Guid> _accountRepository;
    private readonly IReadOnlyRepository<BankCheck, Guid> _checkRepository;
    private readonly IReadOnlyRepository<EftBatch, Guid> _eftBatchRepository;
    private readonly FinanceDocumentHelper _helper;
    private readonly IFinanceDataProtector _protector;

    public BankAccountService(
        IServiceProvider serviceProvider,
        IRepository<BankAccount, Guid> bankAccountRepository,
        IReadOnlyRepository<Account, Guid> accountRepository,
        IReadOnlyRepository<BankCheck, Guid> checkRepository,
        IReadOnlyRepository<EftBatch, Guid> eftBatchRepository,
        FinanceDocumentHelper helper,
        IFinanceDataProtector protector)
        : base(serviceProvider)
    {
        _bankAccountRepository = Check.NotNull(bankAccountRepository);
        _accountRepository = Check.NotNull(accountRepository);
        _checkRepository = Check.NotNull(checkRepository);
        _eftBatchRepository = Check.NotNull(eftBatchRepository);
        _helper = Check.NotNull(helper);
        _protector = Check.NotNull(protector);
    }

    public Task<Result<BankAccountCapabilitiesDto>> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Ok(new BankAccountCapabilitiesDto { CanStoreAccountNumber = _protector.IsConfigured }));

    public async Task<Result<IPagedList<BankAccountDto>>> GetPagedAsync(BankAccountQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var queryable = _bankAccountRepository.AsNoTracking();
        if (query.AccountId.HasValue)
            queryable = queryable.Where(b => b.AccountId == query.AccountId.Value);
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.ToLower();
            queryable = queryable.Where(b => b.Name.ToLower().Contains(keyword) ||
                                             (b.BankName != null && b.BankName.ToLower().Contains(keyword)));
        }

        var pagedList = await queryable
            .OrderByDescending(b => b.CreationTime)
            .ProjectTo<BankAccount, BankAccountDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        await FillAccountNamesAsync(pagedList.Items, cancellationToken);
        return Ok(pagedList);
    }

    public async Task<Result<BankAccountDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _bankAccountRepository.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (entity == null)
            return Fail<BankAccountDto>("Bank account not found.", 404);

        return Ok(await ToDtoAsync(entity, cancellationToken));
    }

    public async Task<Result<BankAccountDto>> CreateAsync(CreateBankAccountDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        if (string.IsNullOrWhiteSpace(input.Name))
            return Fail<BankAccountDto>("Bank account name is required.", 400);
        if (input.NextCheckNumber < 1)
            return Fail<BankAccountDto>("The next check number must be at least 1.", 400);
        var tooLong = FirstTooLong(input.Name, input.BankName, input.Currency, input.CheckTemplateName,
            input.FeedProviderKey, input.ExternalAccountId, input.EftOriginatorName);
        if (tooLong != null)
            return Fail<BankAccountDto>(tooLong, 400);

        var currency = string.IsNullOrWhiteSpace(input.Currency) ? null : input.Currency.Trim().ToUpperInvariant();
        var accountResult = await _helper.GetFundsAccountAsync(input.AccountId, currency, cancellationToken);
        if (!accountResult.Succeeded)
            return Fail<BankAccountDto>(accountResult.Message!, accountResult.Code ?? 400);

        var routingResult = BankNumberHelper.ValidateRouting(input.Scheme, input.RoutingNumber, input.InstitutionNumber, input.TransitNumber);
        if (!routingResult.Succeeded)
            return Fail<BankAccountDto>(routingResult.Message!, routingResult.Code ?? 400);

        var originatorResult = BankNumberHelper.ValidateEftOriginatorId(input.Scheme, input.EftOriginatorId?.Trim());
        if (!originatorResult.Succeeded)
            return Fail<BankAccountDto>(originatorResult.Message!, originatorResult.Code ?? 400);

        var hasExisting = await _bankAccountRepository.AnyAsync(b => b.AccountId == input.AccountId, cancellationToken);
        if (hasExisting)
            return Fail<BankAccountDto>("A bank account is already configured for this ledger account.", 409);

        var entity = new BankAccount
        {
            AccountId = input.AccountId,
            Name = input.Name.Trim(),
            BankName = input.BankName,
            Scheme = input.Scheme,
            RoutingNumber = input.RoutingNumber?.Trim(),
            InstitutionNumber = input.InstitutionNumber?.Trim(),
            TransitNumber = input.TransitNumber?.Trim(),
            Currency = currency,
            NextCheckNumber = input.NextCheckNumber,
            CheckStockType = input.CheckStockType,
            CheckLayout = input.CheckLayout,
            CheckTemplateName = NormalizeTemplateName(input.CheckTemplateName),
            OffsetXMm = input.OffsetXMm,
            OffsetYMm = input.OffsetYMm,
            FeedProviderKey = input.FeedProviderKey?.Trim(),
            ExternalAccountId = input.ExternalAccountId?.Trim(),
            EftOriginatorId = input.EftOriginatorId?.Trim(),
            EftOriginatorName = input.EftOriginatorName?.Trim()
        };

        if (!string.IsNullOrWhiteSpace(input.AccountNumber))
        {
            var protectResult = ProtectAccountNumber(input.AccountNumber, entity);
            if (!protectResult.Succeeded)
                return Fail<BankAccountDto>(protectResult.Message!, protectResult.Code ?? 400);
        }

        try
        {
            await _bankAccountRepository.InsertAsync(entity, cancellationToken);
            await _bankAccountRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<BankAccountDto>("A bank account is already configured for this ledger account.", 409);
        }

        return Ok(await ToDtoAsync(entity, cancellationToken));
    }

    public async Task<Result<BankAccountDto>> UpdateAsync(Guid id, UpdateBankAccountDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var entity = await _bankAccountRepository.AsQueryable(true).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (entity == null)
            return Fail<BankAccountDto>("Bank account not found.", 404);
        if (string.IsNullOrWhiteSpace(input.Name))
            return Fail<BankAccountDto>("Bank account name is required.", 400);

        var tooLong = FirstTooLong(input.Name, input.BankName, input.Currency, input.CheckTemplateName,
            input.FeedProviderKey, input.ExternalAccountId, input.EftOriginatorName);
        if (tooLong != null)
            return Fail<BankAccountDto>(tooLong, 400);

        var routingResult = BankNumberHelper.ValidateRouting(input.Scheme, input.RoutingNumber, input.InstitutionNumber, input.TransitNumber);
        if (!routingResult.Succeeded)
            return Fail<BankAccountDto>(routingResult.Message!, routingResult.Code ?? 400);

        var originatorResult = BankNumberHelper.ValidateEftOriginatorId(input.Scheme, input.EftOriginatorId?.Trim());
        if (!originatorResult.Succeeded)
            return Fail<BankAccountDto>(originatorResult.Message!, originatorResult.Code ?? 400);

        var currency = string.IsNullOrWhiteSpace(input.Currency) ? null : input.Currency.Trim().ToUpperInvariant();
        // 若科目限定币种，档案币种须兼容（挂载科目不可变，此处只校验币种）
        if (currency != null)
        {
            var accountResult = await _helper.GetFundsAccountAsync(entity.AccountId, currency, cancellationToken);
            if (!accountResult.Succeeded)
                return Fail<BankAccountDto>(accountResult.Message!, accountResult.Code ?? 400);
        }

        entity.Name = input.Name.Trim();
        entity.BankName = input.BankName;
        entity.Scheme = input.Scheme;
        entity.RoutingNumber = input.RoutingNumber?.Trim();
        entity.InstitutionNumber = input.InstitutionNumber?.Trim();
        entity.TransitNumber = input.TransitNumber?.Trim();
        entity.Currency = currency;
        entity.CheckStockType = input.CheckStockType;
        entity.CheckLayout = input.CheckLayout;
        entity.CheckTemplateName = NormalizeTemplateName(input.CheckTemplateName);
        entity.OffsetXMm = input.OffsetXMm;
        entity.OffsetYMm = input.OffsetYMm;
        entity.FeedProviderKey = input.FeedProviderKey?.Trim();
        entity.ExternalAccountId = input.ExternalAccountId?.Trim();
        entity.EftOriginatorId = input.EftOriginatorId?.Trim();
        entity.EftOriginatorName = input.EftOriginatorName?.Trim();

        // 明文留空 = 保持现有账号；非空 = 重新加密覆盖
        if (!string.IsNullOrWhiteSpace(input.AccountNumber))
        {
            var protectResult = ProtectAccountNumber(input.AccountNumber, entity);
            if (!protectResult.Succeeded)
                return Fail<BankAccountDto>(protectResult.Message!, protectResult.Code ?? 400);
        }

        try
        {
            await _bankAccountRepository.UpdateAsync(entity, cancellationToken);
            await _bankAccountRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<BankAccountDto>("The bank account was modified by another operation. Reload and retry.", 409);
        }

        return Ok(await ToDtoAsync(entity, cancellationToken));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _bankAccountRepository.AsQueryable(true).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (entity == null)
            return Fail("Bank account not found.", 404);

        // 引用保护：支票登记簿或 EFT 批次引用此档案时拒删（占号/审计留痕不可随档案消失）
        if (await _checkRepository.AnyAsync(c => c.BankAccountId == entity.Id, cancellationToken))
            return Fail("The bank account has checks on record and cannot be deleted.", 409);
        if (await _eftBatchRepository.AnyAsync(b => b.BankAccountId == entity.Id, cancellationToken))
            return Fail("The bank account has EFT batches on record and cannot be deleted.", 409);

        try
        {
            await _bankAccountRepository.DeleteAsync(entity, cancellationToken);
            await _bankAccountRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // 软删是一次带并发标记的整行写：上面的引用检查读完之后，打印 / 登记 / 生成 EFT 的分配器
            // 换掉了标记（它们在推进计数器）。重新加载会看到那张新票或那份新文件，删除随之被引用保护挡住。
            return Fail("The bank account was modified by another operation. Reload and try again.", 409);
        }
        return Ok();
    }

    public async Task<Result<BankAccountDto>> SetNextCheckNumberAsync(Guid id, SetNextCheckNumberDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);
        if (input.NextCheckNumber < 1)
            return Fail<BankAccountDto>("The next check number must be at least 1.", 400);

        var entity = await _bankAccountRepository.AsQueryable(true).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (entity == null)
            return Fail<BankAccountDto>("Bank account not found.", 404);

        // ★ 设到一个已经用过的号上，此后每一次打印都撞（账户, 支票号）唯一索引、回滚又把计数器恢复到
        // 同一个号，永远 409，而重新加载帮不上忙。只拒绝「这个号本身已被占用」：换一本号段更低的
        // 支票本是正常操作，号段之上已有支票不构成拒绝的理由。作废票与毁票同样占号。
        var nextFree = await NextUnusedCheckNumberAsync(entity.Id, input.NextCheckNumber, cancellationToken);
        if (nextFree != input.NextCheckNumber)
        {
            return Fail<BankAccountDto>(
                $"Check number {input.NextCheckNumber} is already used on this bank account. The next unused number from there is {nextFree}.", 400);
        }

        entity.NextCheckNumber = input.NextCheckNumber;
        try
        {
            await _bankAccountRepository.UpdateAsync(entity, cancellationToken);
            await _bankAccountRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<BankAccountDto>("The bank account was modified by another operation. Reload and retry.", 409);
        }

        return Ok(await ToDtoAsync(entity, cancellationToken));
    }

    /// <summary>从 <paramref name="from"/> 起第一个没有被本账户任何支票（含作废票、毁票）占用的号。</summary>
    private async Task<long> NextUnusedCheckNumberAsync(Guid bankAccountId, long from, CancellationToken cancellationToken)
    {
        if (!await _checkRepository.AnyAsync(c => c.BankAccountId == bankAccountId && c.CheckNumber == from, cancellationToken))
            return from;

        var used = await _checkRepository.AsNoTracking()
            .Where(c => c.BankAccountId == bankAccountId && c.CheckNumber >= from)
            .OrderBy(c => c.CheckNumber)
            .Select(c => c.CheckNumber)
            .ToListAsync(cancellationToken);
        var next = from;
        foreach (var number in used)
        {
            if (number > next)
                break;
            next = number + 1;
        }
        return next;
    }

    /// <summary>人工填写的档案字段与列宽比对（按落库前的形状：去首尾空白），返回第一个超长字段的拒绝消息。</summary>
    private static string? FirstTooLong(string name, string? bankName, string? currency, string? checkTemplateName,
        string? feedProviderKey, string? externalAccountId, string? eftOriginatorName)
        => BankingFieldLimits.FirstTooLong(
            (name.Trim(), BankingFieldLimits.BankAccountNameMaxLength, "bank account name"),
            (bankName, BankingFieldLimits.BankNameMaxLength, "bank name"),
            (currency?.Trim(), BankingFieldLimits.CurrencyMaxLength, "currency"),
            (NormalizeTemplateName(checkTemplateName), BankingFieldLimits.CheckTemplateNameMaxLength, "check template name"),
            (feedProviderKey?.Trim(), BankingFieldLimits.FeedProviderKeyMaxLength, "feed provider key"),
            (externalAccountId?.Trim(), BankingFieldLimits.ExternalAccountIdMaxLength, "external account id"),
            (eftOriginatorName?.Trim(), BankingFieldLimits.EftOriginatorNameMaxLength, "EFT originator name"));

    /// <summary>归一化支票模板名：空白 → null（= 回退渲染器默认模板）</summary>
    private static string? NormalizeTemplateName(string? templateName)
        => string.IsNullOrWhiteSpace(templateName) ? null : templateName.Trim();

    /// <summary>加密明文账号并写入密文 + 掩码（未配置密钥返回 400）</summary>
    private Result ProtectAccountNumber(string plaintext, BankAccount entity)
    {
        if (!_protector.IsConfigured)
            return Result.Failure("Configure Finance:Encryption:EncryptionKey before storing bank details.", 400);

        var trimmed = plaintext.Trim();
        // 长度上限来自 EFT 文件的定宽账号字段（CPA-005 的 Originator Account Number 只有 12 位）。
        // 出款方这侧被截断的后果是整份报文从一个不存在的户头扣款 —— 与收款方那侧同样不能静默发生。
        var lengthResult = BankNumberHelper.ValidateAccountNumber(entity.Scheme, trimmed);
        if (!lengthResult.Succeeded)
            return lengthResult;
        // AAD 绑定到本档案的资金科目，密文无法被搬到另一档案复用。
        entity.AccountNumberEncrypted = _protector.Protect(trimmed, FinanceProtectionAad.ForBankAccount(entity.AccountId));
        entity.AccountNumberMasked = BankNumberHelper.Mask(trimmed);
        return Result.Success();
    }

    private async Task<BankAccountDto> ToDtoAsync(BankAccount entity, CancellationToken cancellationToken)
    {
        var dto = entity.MapTo<BankAccountDto>();
        await FillAccountNamesAsync(new List<BankAccountDto> { dto }, cancellationToken);
        return dto;
    }

    private async Task FillAccountNamesAsync(IList<BankAccountDto> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            return;

        var accountIds = items.Select(b => b.AccountId).Distinct().ToList();
        var names = await _accountRepository.AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Code, a.Name })
            .ToDictionaryAsync(a => a.Id, a => $"{a.Code} {a.Name}", cancellationToken);

        foreach (var dto in items)
            dto.AccountName = names.GetValueOrDefault(dto.AccountId);
    }
}
