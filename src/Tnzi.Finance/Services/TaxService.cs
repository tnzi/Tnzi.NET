namespace Tnzi.Finance.Services;

/// <summary>
/// 税模型服务（机构/税率/税码）
/// </summary>
public class TaxService : ApplicationService, ITaxService
{
    private readonly IRepository<TaxAgency, Guid> _agencyRepository;
    private readonly IRepository<TaxRate, Guid> _rateRepository;
    private readonly IRepository<TaxCode, Guid> _codeRepository;
    private readonly IRepository<TaxCodeComponent, Guid> _componentRepository;

    public TaxService(
        IServiceProvider serviceProvider,
        IRepository<TaxAgency, Guid> agencyRepository,
        IRepository<TaxRate, Guid> rateRepository,
        IRepository<TaxCode, Guid> codeRepository,
        IRepository<TaxCodeComponent, Guid> componentRepository)
        : base(serviceProvider)
    {
        _agencyRepository = Check.NotNull(agencyRepository);
        _rateRepository = Check.NotNull(rateRepository);
        _codeRepository = Check.NotNull(codeRepository);
        _componentRepository = Check.NotNull(componentRepository);
    }

    // ── 税务机构 ──────────────────────────────────────────────

    public async Task<Result<List<TaxAgencyDto>>> GetAgenciesAsync(CancellationToken cancellationToken = default)
    {
        var agencies = await _agencyRepository.AsNoTracking()
            .OrderBy(a => a.Name)
            .ProjectTo<TaxAgency, TaxAgencyDto>()
            .ToListAsync(cancellationToken);

        return Ok(agencies);
    }

    public async Task<Result<TaxAgencyDto>> CreateAgencyAsync(UpsertTaxAgencyDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        if (string.IsNullOrWhiteSpace(input.Name))
            return Fail<TaxAgencyDto>("Tax agency name is required.");

        var agency = new TaxAgency
        {
            Name = input.Name.Trim(),
            Description = input.Description,
            IsActive = input.IsActive
        };

        try
        {
            await _agencyRepository.InsertAsync(agency, cancellationToken);
            await _agencyRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<TaxAgencyDto>($"Tax agency '{agency.Name}' already exists.", 409);
        }

        return Ok(agency.MapTo<TaxAgencyDto>());
    }

    public async Task<Result<TaxAgencyDto>> UpdateAgencyAsync(Guid id, UpsertTaxAgencyDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var agency = await _agencyRepository.GetAsync(id, cancellationToken);
        if (agency == null)
            return Fail<TaxAgencyDto>("Tax agency not found.", 404);

        if (string.IsNullOrWhiteSpace(input.Name))
            return Fail<TaxAgencyDto>("Tax agency name is required.");

        agency.Name = input.Name.Trim();
        agency.Description = input.Description;
        agency.IsActive = input.IsActive;

        try
        {
            await _agencyRepository.UpdateAsync(agency, cancellationToken);
            await _agencyRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<TaxAgencyDto>($"Tax agency '{agency.Name}' already exists.", 409);
        }

        return Ok(agency.MapTo<TaxAgencyDto>());
    }

    public async Task<Result> DeleteAgencyAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var agency = await _agencyRepository.GetAsync(id, cancellationToken);
        if (agency == null)
            return Fail("Tax agency not found.", 404);

        if (await _rateRepository.AnyAsync(r => r.AgencyId == id, cancellationToken))
            return Fail("The tax agency is referenced by tax rates and cannot be deleted.", 409);

        await _agencyRepository.DeleteAsync(agency, cancellationToken);
        return Ok();
    }

    // ── 税率 ─────────────────────────────────────────────────

    public async Task<Result<List<TaxRateDto>>> GetRatesAsync(Guid? agencyId = null, CancellationToken cancellationToken = default)
    {
        var query = _rateRepository.AsNoTracking();
        if (agencyId.HasValue)
            query = query.Where(r => r.AgencyId == agencyId.Value);

        var rates = await query
            .OrderBy(r => r.Name)
            .Select(r => new TaxRateDto
            {
                Id = r.Id,
                AgencyId = r.AgencyId,
                AgencyName = r.Agency!.Name,
                Name = r.Name,
                Rate = r.Rate,
                IsActive = r.IsActive
            })
            .ToListAsync(cancellationToken);

        return Ok(rates);
    }

    public async Task<Result<TaxRateDto>> CreateRateAsync(UpsertTaxRateDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var validation = await ValidateRateAsync(input, cancellationToken);
        if (!validation.Succeeded)
            return Fail<TaxRateDto>(validation.Message ?? "Invalid tax rate.", validation.Code ?? 400);

        var rate = new TaxRate
        {
            AgencyId = input.AgencyId,
            Name = input.Name.Trim(),
            Rate = input.Rate,
            IsActive = input.IsActive
        };

        await _rateRepository.InsertAsync(rate, cancellationToken);
        await _rateRepository.SaveChangesAsync(cancellationToken);

        return Ok(rate.MapTo<TaxRateDto>());
    }

    public async Task<Result<TaxRateDto>> UpdateRateAsync(Guid id, UpsertTaxRateDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var rate = await _rateRepository.GetAsync(id, cancellationToken);
        if (rate == null)
            return Fail<TaxRateDto>("Tax rate not found.", 404);

        var validation = await ValidateRateAsync(input, cancellationToken);
        if (!validation.Succeeded)
            return Fail<TaxRateDto>(validation.Message ?? "Invalid tax rate.", validation.Code ?? 400);

        rate.AgencyId = input.AgencyId;
        rate.Name = input.Name.Trim();
        rate.Rate = input.Rate;
        rate.IsActive = input.IsActive;

        await _rateRepository.UpdateAsync(rate, cancellationToken);
        return Ok(rate.MapTo<TaxRateDto>());
    }

    public async Task<Result> DeleteRateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rate = await _rateRepository.GetAsync(id, cancellationToken);
        if (rate == null)
            return Fail("Tax rate not found.", 404);

        if (await _componentRepository.AnyAsync(c => c.TaxRateId == id, cancellationToken))
            return Fail("The tax rate is referenced by tax codes and cannot be deleted.", 409);

        await _rateRepository.DeleteAsync(rate, cancellationToken);
        return Ok();
    }

    private async Task<Result> ValidateRateAsync(UpsertTaxRateDto input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return Fail("Tax rate name is required.");
        if (input.Rate < 0)
            return Fail("Tax rate must not be negative.");
        if (!await _agencyRepository.AnyAsync(a => a.Id == input.AgencyId, cancellationToken))
            return Fail("Tax agency not found.", 404);

        return Ok();
    }

    // ── 税码 ─────────────────────────────────────────────────

    public async Task<Result<List<TaxCodeDto>>> GetCodesAsync(CancellationToken cancellationToken = default)
    {
        var codes = await _codeRepository.AsNoTracking()
            .Include(c => c.Components.OrderBy(x => x.Order))
            .ThenInclude(c => c.Rate)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        return Ok(codes.Select(ToDto).ToList());
    }

    public async Task<Result<TaxCodeDto>> CreateCodeAsync(UpsertTaxCodeDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var validation = await ValidateCodeAsync(input, cancellationToken);
        if (!validation.Succeeded)
            return Fail<TaxCodeDto>(validation.Message ?? "Invalid tax code.", validation.Code ?? 400);

        var code = new TaxCode
        {
            Name = input.Name.Trim(),
            Description = input.Description,
            IsActive = input.IsActive
        };

        foreach (var component in input.Components)
        {
            code.Components.Add(new TaxCodeComponent
            {
                TaxRateId = component.TaxRateId,
                Order = component.Order,
                IsCompound = component.IsCompound
            });
        }

        try
        {
            await _codeRepository.InsertAsync(code, cancellationToken);
            await _codeRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<TaxCodeDto>($"Tax code '{code.Name}' already exists.", 409);
        }

        return await ReloadCodeAsync(code.Id, cancellationToken);
    }

    public async Task<Result<TaxCodeDto>> UpdateCodeAsync(Guid id, UpsertTaxCodeDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var code = await _codeRepository.AsQueryable(true)
            .Include(c => c.Components)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (code == null)
            return Fail<TaxCodeDto>("Tax code not found.", 404);

        var validation = await ValidateCodeAsync(input, cancellationToken);
        if (!validation.Succeeded)
            return Fail<TaxCodeDto>(validation.Message ?? "Invalid tax code.", validation.Code ?? 400);

        code.Name = input.Name.Trim();
        code.Description = input.Description;
        code.IsActive = input.IsActive;

        // 组件全量替换（硬删重建，与草稿行范式一致）
        if (code.Components.Count > 0)
            await _componentRepository.DeleteManyAsync(code.Components.ToList(), cancellationToken);
        code.Components.Clear();

        foreach (var component in input.Components)
        {
            code.Components.Add(new TaxCodeComponent
            {
                TaxCodeId = code.Id,
                TaxRateId = component.TaxRateId,
                Order = component.Order,
                IsCompound = component.IsCompound
            });
        }

        try
        {
            await _codeRepository.UpdateAsync(code, cancellationToken);
            await _codeRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<TaxCodeDto>($"Tax code '{code.Name}' already exists.", 409);
        }

        return await ReloadCodeAsync(code.Id, cancellationToken);
    }

    public async Task<Result> DeleteCodeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var code = await _codeRepository.AsQueryable(true)
            .Include(c => c.Components)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (code == null)
            return Fail("Tax code not found.", 404);

        // P2b：被单据行引用时拒绝删除
        // 组件无软删除，随税码一并物理删除——否则残留组件会永久阻塞税率删除
        if (code.Components.Count > 0)
            await _componentRepository.DeleteManyAsync(code.Components.ToList(), cancellationToken);

        await _codeRepository.DeleteAsync(code, cancellationToken);
        return Ok();
    }

    private async Task<Result> ValidateCodeAsync(UpsertTaxCodeDto input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return Fail("Tax code name is required.");
        if (input.Components == null || input.Components.Count == 0)
            return Fail("At least one tax rate component is required.");

        var rateIds = input.Components.Select(c => c.TaxRateId).ToList();
        if (rateIds.Distinct().Count() != rateIds.Count)
            return Fail("Duplicate tax rates in components.");

        var existing = await _rateRepository.AsNoTracking()
            .Where(r => rateIds.Contains(r.Id))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);
        if (existing.Count != rateIds.Count)
            return Fail("One or more tax rates do not exist.", 404);

        return Ok();
    }

    private async Task<Result<TaxCodeDto>> ReloadCodeAsync(Guid id, CancellationToken cancellationToken)
    {
        var reloaded = await _codeRepository.AsNoTracking()
            .Include(c => c.Components.OrderBy(x => x.Order))
            .ThenInclude(c => c.Rate)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        return reloaded == null
            ? Fail<TaxCodeDto>("Tax code not found.", 404)
            : Ok(ToDto(reloaded));
    }

    private static TaxCodeDto ToDto(TaxCode code) => new()
    {
        Id = code.Id,
        Name = code.Name,
        Description = code.Description,
        IsActive = code.IsActive,
        Components = code.Components
            .OrderBy(c => c.Order)
            .Select(c => new TaxCodeComponentDto
            {
                TaxRateId = c.TaxRateId,
                RateName = c.Rate?.Name,
                Rate = c.Rate?.Rate ?? 0m,
                Order = c.Order,
                IsCompound = c.IsCompound
            })
            .ToList()
    };
}
