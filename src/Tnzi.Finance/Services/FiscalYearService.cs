namespace Tnzi.Finance.Services;

/// <summary>
/// 会计年度服务（锁定日期模型）
/// </summary>
public class FiscalYearService : ApplicationService, IFiscalYearService
{
    private readonly IRepository<FiscalYear, Guid> _fiscalYearRepository;
    private readonly FinanceOptions _options;

    public FiscalYearService(
        IServiceProvider serviceProvider,
        IRepository<FiscalYear, Guid> fiscalYearRepository,
        IOptions<FinanceOptions> options)
        : base(serviceProvider)
    {
        _fiscalYearRepository = Check.NotNull(fiscalYearRepository);
        _options = Check.NotNull(options).Value;
    }

    public async Task<Result<List<FiscalYearDto>>> GetListAsync(CancellationToken cancellationToken = default)
    {
        var years = await _fiscalYearRepository.AsNoTracking()
            .OrderByDescending(f => f.StartDate)
            .ProjectTo<FiscalYear, FiscalYearDto>()
            .ToListAsync(cancellationToken);

        return Ok(years);
    }

    public async Task<Result<FiscalYearDto>> CreateAsync(CreateFiscalYearDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        if (string.IsNullOrWhiteSpace(input.Name))
            return Fail<FiscalYearDto>("Fiscal year name is required.");

        var start = input.StartDate.ToUtcDate();
        var end = input.EndDate.ToUtcDate();
        if (end <= start)
            return Fail<FiscalYearDto>("EndDate must be after StartDate.");

        var name = input.Name.Trim();
        if (await _fiscalYearRepository.AnyAsync(f => f.Name == name, cancellationToken))
            return Fail<FiscalYearDto>($"Fiscal year '{name}' already exists.", 409);

        if (await _fiscalYearRepository.AnyAsync(f => f.StartDate <= end && f.EndDate >= start, cancellationToken))
            return Fail<FiscalYearDto>("The date range overlaps with an existing fiscal year.", 409);

        var fiscalYear = new FiscalYear
        {
            Name = name,
            StartDate = start,
            EndDate = end
        };

        try
        {
            await _fiscalYearRepository.InsertAsync(fiscalYear, cancellationToken);
            await _fiscalYearRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<FiscalYearDto>($"Fiscal year '{name}' already exists.", 409);
        }

        return Ok(fiscalYear.MapTo<FiscalYearDto>());
    }

    public async Task<Result> CloseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var fiscalYear = await _fiscalYearRepository.GetAsync(id, cancellationToken);
        if (fiscalYear == null)
            return Fail("Fiscal year not found.", 404);
        if (fiscalYear.IsClosed)
            return Fail("Fiscal year is already closed.", 409);

        fiscalYear.IsClosed = true;
        fiscalYear.ClosedTime = TimeProvider.GetUtcNow().UtcDateTime;
        fiscalYear.ClosedById = CurrentUser?.Id;

        await _fiscalYearRepository.UpdateAsync(fiscalYear, cancellationToken);
        return Ok();
    }

    public async Task<Result> ReopenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var fiscalYear = await _fiscalYearRepository.GetAsync(id, cancellationToken);
        if (fiscalYear == null)
            return Fail("Fiscal year not found.", 404);
        if (!fiscalYear.IsClosed)
            return Fail("Fiscal year is not closed.", 409);

        fiscalYear.IsClosed = false;
        fiscalYear.ClosedTime = null;
        fiscalYear.ClosedById = null;

        await _fiscalYearRepository.UpdateAsync(fiscalYear, cancellationToken);
        return Ok();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var fiscalYear = await _fiscalYearRepository.GetAsync(id, cancellationToken);
        if (fiscalYear == null)
            return Fail("Fiscal year not found.", 404);

        await _fiscalYearRepository.DeleteAsync(fiscalYear, cancellationToken);
        return Ok();
    }

    public async Task<Result> ValidatePostingDateAsync(DateTime postingDate, CancellationToken cancellationToken = default)
    {
        var date = postingDate.ToUtcDate();

        var closedYearName = await _fiscalYearRepository.AsNoTracking()
            .Where(f => f.IsClosed && f.StartDate <= date && f.EndDate >= date)
            .Select(f => f.Name)
            .FirstOrDefaultAsync(cancellationToken);

        if (closedYearName != null)
            return Fail($"Posting date {date:yyyy-MM-dd} falls within the closed fiscal year '{closedYearName}'.", 409);

        if (_options.RequireFiscalYearForPosting)
        {
            var inOpenYear = await _fiscalYearRepository.AnyAsync(
                f => !f.IsClosed && f.StartDate <= date && f.EndDate >= date, cancellationToken);

            if (!inOpenYear)
                return Fail($"Posting date {date:yyyy-MM-dd} does not fall within any open fiscal year.");
        }

        return Ok();
    }
}
