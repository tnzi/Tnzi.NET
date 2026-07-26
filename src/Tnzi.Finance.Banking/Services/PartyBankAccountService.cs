namespace Tnzi.Finance.Banking.Services;

/// <summary>
/// 往来方银行账户服务
/// </summary>
/// <remarks>
/// 账号明文单向入库（加密到 <c>AccountNumberEncrypted</c> + 留掩码），DTO 永不回明文。
/// 每个往来方至多一个默认账户：置默认时在同一工作单元内清除同方旧默认。
/// </remarks>
public class PartyBankAccountService : ApplicationService, IPartyBankAccountService
{
    private readonly IRepository<PartyBankAccount, Guid> _repository;
    private readonly IFinanceDataProtector _protector;

    public PartyBankAccountService(
        IServiceProvider serviceProvider,
        IRepository<PartyBankAccount, Guid> repository,
        IFinanceDataProtector protector)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _protector = Check.NotNull(protector);
    }

    public async Task<Result<IPagedList<PartyBankAccountDto>>> GetPagedAsync(PartyBankAccountQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var queryable = _repository.AsNoTracking();
        if (query.PartyType.HasValue)
            queryable = queryable.Where(p => p.PartyType == query.PartyType.Value);
        if (query.PartyId.HasValue)
            queryable = queryable.Where(p => p.PartyId == query.PartyId.Value);
        if (query.IsActive.HasValue)
            queryable = queryable.Where(p => p.IsActive == query.IsActive.Value);

        var pagedList = await queryable
            .OrderByDescending(p => p.IsDefault)
            .ThenByDescending(p => p.CreationTime)
            .ProjectTo<PartyBankAccount, PartyBankAccountDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        return Ok(pagedList);
    }

    public async Task<Result<PartyBankAccountDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _repository.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (entity == null)
            return Fail<PartyBankAccountDto>("Party bank account not found.", 404);
        return Ok(entity.MapTo<PartyBankAccountDto>());
    }

    public async Task<Result<List<PartyBankAccountDto>>> GetByPartyAsync(FinancePartyType partyType, Guid partyId, CancellationToken cancellationToken = default)
    {
        var list = await _repository.AsNoTracking()
            .Where(p => p.PartyType == partyType && p.PartyId == partyId)
            .OrderByDescending(p => p.IsDefault)
            .ThenByDescending(p => p.CreationTime)
            .ProjectTo<PartyBankAccount, PartyBankAccountDto>()
            .ToListAsync(cancellationToken);
        return Ok(list);
    }

    public async Task<Result<PartyBankAccountDto>> CreateAsync(SavePartyBankAccountDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var validation = Validate(input);
        if (!validation.Succeeded)
            return Fail<PartyBankAccountDto>(validation.Message!, validation.Code ?? 400);

        var entity = new PartyBankAccount
        {
            PartyType = input.PartyType,
            PartyId = input.PartyId
        };
        var applyResult = ApplyInput(entity, input, requireAccountNumber: false);
        if (!applyResult.Succeeded)
            return Fail<PartyBankAccountDto>(applyResult.Message!, applyResult.Code ?? 400);

        Guid newId = Guid.Empty;
        try
        {
            await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                // 先清掉该往来方所有旧默认并落盘，再插入新默认——保证任一时刻至多一行 IsDefault=true，
                // 不与过滤唯一索引在同事务内撞车（EF 会把 INSERT 排在 UPDATE 之前，故须显式先落盘）。
                if (entity.IsDefault)
                    await ClearAllDefaultsAndFlushAsync(entity.PartyType, entity.PartyId, ct);
                await _repository.InsertAsync(entity, ct);
                newId = entity.Id;
                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<PartyBankAccountDto>("Another default account was set for this party concurrently. Reload and retry.", 409);
        }

        return await GetAsync(newId, cancellationToken);
    }

    public async Task<Result<PartyBankAccountDto>> UpdateAsync(Guid id, SavePartyBankAccountDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var entity = await _repository.AsQueryable(true).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (entity == null)
            return Fail<PartyBankAccountDto>("Party bank account not found.", 404);

        var validation = Validate(input);
        if (!validation.Succeeded)
            return Fail<PartyBankAccountDto>(validation.Message!, validation.Code ?? 400);

        var applyResult = ApplyInput(entity, input, requireAccountNumber: false);
        if (!applyResult.Succeeded)
            return Fail<PartyBankAccountDto>(applyResult.Message!, applyResult.Code ?? 400);

        try
        {
            await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                if (input.IsDefault)
                    await MakeSoleDefaultAsync(entity, ct);
                else
                    await _repository.UpdateAsync(entity, ct);
                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<PartyBankAccountDto>("Another default account was set for this party concurrently. Reload and retry.", 409);
        }

        return await GetAsync(entity.Id, cancellationToken);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _repository.AsQueryable(true).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (entity == null)
            return Fail("Party bank account not found.", 404);

        await _repository.DeleteAsync(entity, cancellationToken);
        return Ok();
    }

    public async Task<Result<PartyBankAccountDto>> SetDefaultAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _repository.AsQueryable(true).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (entity == null)
            return Fail<PartyBankAccountDto>("Party bank account not found.", 404);

        try
        {
            await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                await MakeSoleDefaultAsync(entity, ct);
                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<PartyBankAccountDto>("Another default account was set for this party concurrently. Reload and retry.", 409);
        }

        return await GetAsync(entity.Id, cancellationToken);
    }

    /// <summary>
    /// 把 <paramref name="entity"/> 设为该往来方唯一默认（UoW 内）：先把所有默认（含 self）落为 false 并落盘，
    /// 再单独把 self 置 true——保证任一时刻至多一行 IsDefault=true，不与过滤唯一索引在同事务内撞车。
    /// </summary>
    private async Task MakeSoleDefaultAsync(PartyBankAccount entity, CancellationToken cancellationToken)
    {
        var others = await _repository.ToListAsync(
            p => p.PartyType == entity.PartyType && p.PartyId == entity.PartyId && p.Id != entity.Id && p.IsDefault, cancellationToken);
        foreach (var other in others)
            other.IsDefault = false;
        entity.IsDefault = false;
        if (others.Count > 0)
            await _repository.UpdateManyAsync(others, cancellationToken);
        await _repository.UpdateAsync(entity, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken); // 全部落为 false 后落盘
        entity.IsDefault = true;
        await _repository.UpdateAsync(entity, cancellationToken); // 再单独置 self 为唯一默认
    }

    /// <summary>把该往来方所有默认落为 false 并落盘（供 Create 在插入新默认前清场）。</summary>
    private async Task ClearAllDefaultsAndFlushAsync(FinancePartyType partyType, Guid partyId, CancellationToken cancellationToken)
    {
        var others = await _repository.ToListAsync(
            p => p.PartyType == partyType && p.PartyId == partyId && p.IsDefault, cancellationToken);
        if (others.Count == 0)
            return;
        foreach (var other in others)
            other.IsDefault = false;
        await _repository.UpdateManyAsync(others, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
    }

    private static Result Validate(SavePartyBankAccountDto input)
    {
        if (input.PartyId == Guid.Empty)
            return Result.Failure("A party is required.", 400);
        return BankNumberHelper.ValidateRouting(input.Scheme, input.RoutingNumber, input.InstitutionNumber, input.TransitNumber);
    }

    private Result ApplyInput(PartyBankAccount entity, SavePartyBankAccountDto input, bool requireAccountNumber)
    {
        entity.Label = input.Label;
        entity.BankName = input.BankName;
        entity.Scheme = input.Scheme;
        entity.RoutingNumber = input.RoutingNumber?.Trim();
        entity.InstitutionNumber = input.InstitutionNumber?.Trim();
        entity.TransitNumber = input.TransitNumber?.Trim();
        entity.AccountType = input.AccountType;
        entity.Currency = string.IsNullOrWhiteSpace(input.Currency) ? null : input.Currency.Trim().ToUpperInvariant();
        entity.IsDefault = input.IsDefault;
        entity.IsActive = input.IsActive;
        entity.Notes = input.Notes;

        if (!string.IsNullOrWhiteSpace(input.AccountNumber))
        {
            if (!_protector.IsConfigured)
                return Result.Failure("Configure Finance:Encryption:EncryptionKey before storing bank details.", 400);
            var trimmed = input.AccountNumber.Trim();
            // AAD 绑定到该往来方，密文无法被搬到另一个往来方复用。
            entity.AccountNumberEncrypted = _protector.Protect(trimmed, FinanceProtectionAad.ForPartyBankAccount(entity.PartyType, entity.PartyId));
            entity.AccountNumberMasked = BankNumberHelper.Mask(trimmed);
        }
        else if (requireAccountNumber)
        {
            return Result.Failure("An account number is required.", 400);
        }

        return Result.Success();
    }
}
