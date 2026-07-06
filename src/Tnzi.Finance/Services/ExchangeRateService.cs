namespace Tnzi.Finance.Services;

/// <summary>
/// 汇率服务
/// </summary>
public class ExchangeRateService : ApplicationService, IExchangeRateService
{
    private readonly IRepository<ExchangeRate, Guid> _rateRepository;
    private readonly FinanceOptions _options;
    private readonly IExchangeRateProvider? _provider;

    public ExchangeRateService(
        IServiceProvider serviceProvider,
        IRepository<ExchangeRate, Guid> rateRepository,
        IOptions<FinanceOptions> options,
        IExchangeRateProvider? provider = null)
        : base(serviceProvider)
    {
        _rateRepository = Check.NotNull(rateRepository);
        _options = Check.NotNull(options).Value;
        _provider = provider;
    }

    public async Task<decimal?> ResolveRateAsync(string fromCurrency, string toCurrency, DateTime date, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(fromCurrency);
        Check.NotNullOrWhiteSpace(toCurrency);

        var from = fromCurrency.Trim().ToUpperInvariant();
        var to = toCurrency.Trim().ToUpperInvariant();
        if (from == to)
            return 1m;

        // date-only 语义 + Utc Kind（PostgreSQL timestamptz 参数要求）
        var targetDate = date.ToUtcDate();

        // "不晚于目标日期的最近一条"生效汇率
        Task<decimal?> LatestRateAsync(string f, string t) => _rateRepository.AsNoTracking()
            .Where(r => r.FromCurrency == f && r.ToCurrency == t && r.RateDate <= targetDate)
            .OrderByDescending(r => r.RateDate)
            .Select(r => (decimal?)r.Rate)
            .FirstOrDefaultAsync(cancellationToken);

        // 直接汇率优先
        var direct = await LatestRateAsync(from, to);
        if (direct.HasValue)
            return direct.Value;

        // 反向汇率取倒数
        var inverse = await LatestRateAsync(to, from);
        if (inverse is > 0)
            return 1m / inverse.Value;

        return null;
    }

    public async Task<Result<IPagedList<ExchangeRateDto>>> GetListAsync(ExchangeRateQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var pagedList = await _rateRepository.AsNoTracking()
            .Filter(query)
            .OrderByDescending(r => r.RateDate)
            .ThenBy(r => r.FromCurrency)
            .ProjectTo<ExchangeRate, ExchangeRateDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        return Ok(pagedList);
    }

    public async Task<Result<ExchangeRateDto>> UpsertAsync(UpsertExchangeRateDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        if (string.IsNullOrWhiteSpace(input.FromCurrency) || string.IsNullOrWhiteSpace(input.ToCurrency))
            return Fail<ExchangeRateDto>("FromCurrency and ToCurrency are required.");
        if (input.Rate <= 0)
            return Fail<ExchangeRateDto>("Rate must be greater than 0.");

        var from = input.FromCurrency.Trim().ToUpperInvariant();
        var to = input.ToCurrency.Trim().ToUpperInvariant();
        if (from == to)
            return Fail<ExchangeRateDto>("FromCurrency and ToCurrency must be different.");
        if (from.Length > 8 || to.Length > 8)
            return Fail<ExchangeRateDto>("Currency codes must not exceed 8 characters.");

        var rateDate = input.RateDate.ToUtcDate();
        var entity = await _rateRepository.FindAsync(
            r => r.FromCurrency == from && r.ToCurrency == to && r.RateDate == rateDate, cancellationToken);

        try
        {
            if (entity == null)
            {
                entity = new ExchangeRate
                {
                    FromCurrency = from,
                    ToCurrency = to,
                    Rate = input.Rate,
                    RateDate = rateDate,
                    Source = input.Source ?? "Manual"
                };
                await _rateRepository.InsertAsync(entity, cancellationToken);
            }
            else
            {
                entity.Rate = input.Rate;
                entity.Source = input.Source ?? entity.Source;
                await _rateRepository.UpdateAsync(entity, cancellationToken);
            }

            await _rateRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            // 并发 Upsert 同一 (币种对, 日期)：由唯一索引兜底，提示重试
            return Fail<ExchangeRateDto>("The exchange rate was created concurrently. Retry the operation.", 409);
        }

        return Ok(entity.MapTo<ExchangeRateDto>());
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _rateRepository.GetAsync(id, cancellationToken);
        if (entity == null)
            return Fail("Exchange rate not found.", 404);

        await _rateRepository.DeleteAsync(entity, cancellationToken);
        return Ok();
    }

    public async Task<Result<int>> RefreshFromProviderAsync(CancellationToken cancellationToken = default)
    {
        if (_provider == null)
        {
            return Fail<int>(
                "No exchange rate provider is registered. Register an IExchangeRateProvider implementation to enable refresh.",
                501);
        }

        var quotes = await _provider.GetLatestRatesAsync(_options.BaseCurrency, cancellationToken);
        var count = 0;

        foreach (var quote in quotes)
        {
            var result = await UpsertAsync(new UpsertExchangeRateDto
            {
                FromCurrency = quote.FromCurrency,
                ToCurrency = quote.ToCurrency,
                Rate = quote.Rate,
                RateDate = quote.RateDate,
                Source = _provider.GetType().Name
            }, cancellationToken);

            if (result.Succeeded)
                count++;
            else
                LogWarning("Skipped invalid exchange rate quote {From}->{To}: {Message}", quote.FromCurrency, quote.ToCurrency, result.Message ?? "unknown error");
        }

        return Ok(count);
    }
}
