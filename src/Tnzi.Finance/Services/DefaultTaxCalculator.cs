namespace Tnzi.Finance.Services;

/// <summary>
/// 默认税额计算器：行级百分比 + 复合税，AwayFromZero 舍入
/// </summary>
/// <remarks>
/// 每行按税码组件顺序计算：非复合组件税基 = 行金额；复合组件税基 = 行金额 + 该行前序组件税额。
/// 每个组件税额行级舍入后按税率维度聚合。停用或不存在的税码抛 <see cref="BusinessException"/>。
/// </remarks>
public class DefaultTaxCalculator : ITaxCalculator
{
    private readonly IReadOnlyRepository<TaxCode, Guid> _codeRepository;
    private readonly FinanceOptions _options;

    public DefaultTaxCalculator(IReadOnlyRepository<TaxCode, Guid> codeRepository, IOptionsSnapshot<FinanceOptions> options)
    {
        _codeRepository = Check.NotNull(codeRepository);
        _options = Check.NotNull(options).Value;
    }

    public async Task<TaxCalculationResult> CalculateAsync(TaxCalculationRequest request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);
        Check.NotNull(request.Lines);

        var decimals = request.Decimals ?? _options.BaseCurrencyDecimals;
        var result = new TaxCalculationResult();

        var codeIds = request.Lines
            .Where(l => l.TaxCodeId.HasValue)
            .Select(l => l.TaxCodeId!.Value)
            .Distinct()
            .ToList();

        if (codeIds.Count == 0)
            return result;

        var codes = await _codeRepository.AsNoTracking()
            .Include(c => c.Components.OrderBy(x => x.Order))
            .ThenInclude(c => c.Rate)
            .Where(c => codeIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        var totals = new Dictionary<Guid, TaxComponentAmount>();

        foreach (var line in request.Lines)
        {
            if (!line.TaxCodeId.HasValue)
                continue;

            if (!codes.TryGetValue(line.TaxCodeId.Value, out var code))
                throw new BusinessException($"Tax code '{line.TaxCodeId}' not found.", httpStatusCode: 404);
            if (!code.IsActive)
                throw new BusinessException($"Tax code '{code.Name}' is inactive.");

            var accumulatedTax = 0m;
            foreach (var component in code.Components.OrderBy(c => c.Order))
            {
                var rate = component.Rate;
                if (rate == null || !rate.IsActive)
                    throw new BusinessException($"Tax rate for code '{code.Name}' is missing or inactive.");

                var baseAmount = component.IsCompound ? line.Amount + accumulatedTax : line.Amount;
                var tax = Math.Round(baseAmount * rate.Rate / 100m, decimals, MidpointRounding.AwayFromZero);
                accumulatedTax += tax;

                if (!totals.TryGetValue(rate.Id, out var bucket))
                {
                    bucket = new TaxComponentAmount
                    {
                        TaxRateId = rate.Id,
                        AgencyId = rate.AgencyId,
                        RateName = rate.Name,
                        Rate = rate.Rate
                    };
                    totals[rate.Id] = bucket;
                }

                bucket.TaxAmount += tax;
            }

            result.TaxTotal += accumulatedTax;
        }

        result.Components = totals.Values.OrderBy(c => c.RateName).ToList();
        return result;
    }
}
