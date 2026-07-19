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
        IOptionsSnapshot<FinanceOptions> options,
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
        var source = _provider.GetType().Name;

        // 本地先校验/归一化(与 UpsertAsync 同规则),无效报价记警告跳过
        var validQuotes = new List<(string From, string To, decimal Rate, DateTime RateDate)>();
        foreach (var quote in quotes)
        {
            var error = ValidateQuote(quote, out var normalized);
            if (error == null)
                validQuotes.Add(normalized);
            else
                LogWarning("Skipped invalid exchange rate quote {From}->{To}: {Message}", quote.FromCurrency, quote.ToCurrency, error);
        }

        if (validQuotes.Count == 0)
            return Ok(0);

        // 同 (from, to, date) 元组重复报价去重,后者胜(对齐逐条 upsert 的覆盖语义)
        validQuotes = validQuotes
            .GroupBy(q => (q.From, q.To, q.RateDate))
            .Select(g => g.Last())
            .ToList();

        // 单查预加载全部命中的既有汇率(替代逐报价 FindAsync 的 2N 往返);
        // 按三列 IN 过滤是超集,精确 (from, to, date) 元组在内存收敛
        var froms = validQuotes.Select(q => q.From).Distinct().ToList();
        var tos = validQuotes.Select(q => q.To).Distinct().ToList();
        var dates = validQuotes.Select(q => q.RateDate).Distinct().ToList();
        var existing = await _rateRepository
            .Where(r => froms.Contains(r.FromCurrency) && tos.Contains(r.ToCurrency) && dates.Contains(r.RateDate))
            .ToListAsync(cancellationToken);
        var existingLookup = existing.ToDictionary(r => (r.FromCurrency, r.ToCurrency, r.RateDate));

        var inserts = new List<ExchangeRate>();
        foreach (var (from, to, rate, rateDate) in validQuotes)
        {
            if (existingLookup.TryGetValue((from, to, rateDate), out var entity))
            {
                entity.Rate = rate;
                entity.Source = source;
                await _rateRepository.UpdateAsync(entity, cancellationToken);
            }
            else
            {
                inserts.Add(new ExchangeRate
                {
                    FromCurrency = from,
                    ToCurrency = to,
                    Rate = rate,
                    RateDate = rateDate,
                    Source = source
                });
            }
        }

        try
        {
            if (inserts.Count > 0)
                await _rateRepository.InsertManyAsync(inserts, cancellationToken);
            await _rateRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            // 并发刷新同一 (币种对, 日期)：由唯一索引兜底，提示重试
            return Fail<int>("Exchange rates were refreshed concurrently. Retry the operation.", 409);
        }

        return Ok(validQuotes.Count);
    }

    /// <summary>
    /// 报价校验与归一化(与 UpsertAsync 同规则);返回 null 表示有效
    /// </summary>
    private static string? ValidateQuote(ExchangeRateQuote quote, out (string From, string To, decimal Rate, DateTime RateDate) normalized)
    {
        normalized = default;

        if (string.IsNullOrWhiteSpace(quote.FromCurrency) || string.IsNullOrWhiteSpace(quote.ToCurrency))
            return "FromCurrency and ToCurrency are required.";
        if (quote.Rate <= 0)
            return "Rate must be greater than 0.";

        var from = quote.FromCurrency.Trim().ToUpperInvariant();
        var to = quote.ToCurrency.Trim().ToUpperInvariant();
        if (from == to)
            return "FromCurrency and ToCurrency must be different.";
        if (from.Length > 8 || to.Length > 8)
            return "Currency codes must not exceed 8 characters.";

        normalized = (from, to, quote.Rate, quote.RateDate.ToUtcDate());
        return null;
    }
}
