namespace Tnzi.Finance.Services;

/// <summary>
/// 默认税额计算器：行级百分比 + 复合税，AwayFromZero 舍入
/// </summary>
/// <remarks>
/// 每行按税码组件顺序计算：非复合组件税基 = 行金额；复合组件税基 = 行金额 + 该行前序组件税额。
/// 每个组件税额行级舍入后按税率维度聚合。停用或不存在的税码抛 <see cref="BusinessException"/>。
/// 行给出手动税额覆盖（<see cref="TaxCalculationLine.TaxAmount"/>）时，行税额 = 覆盖额，
/// 按正常口径的组件税额比例分摊到各组件（行级舍入，尾差归最后一个组件），保证组件合计恰等于覆盖额；
/// 覆盖必须依附税码（税务申报按税率维度聚合，无税码的覆盖额会漏出申报口径），且不得为负。
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

        // 覆盖合法性先于免税提前返回（无税码行带覆盖同样必须被拒绝）
        foreach (var line in request.Lines)
        {
            if (line.TaxAmount < 0)
                throw new BusinessException("A manual tax amount must not be negative.");
            if (line.TaxAmount.HasValue && !line.TaxCodeId.HasValue)
                throw new BusinessException("A manual tax amount requires a tax code (tax totals are reported by tax rate).");
        }

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

            // 先按正常口径算出各组件税额（行级舍入）
            var componentTaxes = CalculateComponents(code, line.Amount, decimals, out var lineTax);

            // 手动覆盖：行税额 = 覆盖额，按正常口径比例分摊到各组件
            if (line.TaxAmount.HasValue)
            {
                var overrideAmount = Math.Round(line.TaxAmount.Value, decimals, MidpointRounding.AwayFromZero);
                ApplyOverride(componentTaxes, overrideAmount, lineTax, decimals);
                lineTax = overrideAmount;
            }

            // 不可抵扣税码（如美国销售税/不可抵扣 VAT）：仅采购单据（IsPurchase）应用抵扣判定——
            // 行税额作为成本累加到 NonRecoverableTotal（过入 NonRecoverableTaxExpense 费用科目），不进 TaxReceivable。
            // 销售侧（发票/贷项）税为销项，无论 IsRecoverable 全额进 Components（TaxPayable）。
            if (!request.IsPurchase || code.IsRecoverable)
            {
                foreach (var (rate, tax) in componentTaxes)
                {
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
            }
            else
            {
                result.NonRecoverableTotal += lineTax;
            }

            // TaxTotal 始终含全部税（单据总额 = 小计 + 全部税，向往来方收/付）
            result.TaxTotal += lineTax;
        }

        result.Components = totals.Values.OrderBy(c => c.RateName).ToList();
        return result;
    }

    /// <summary>按税码组件顺序计算单行各组件税额（复合组件税基含前序税额），返回组件税额列表并输出行税额合计</summary>
    private static List<(TaxRate Rate, decimal Tax)> CalculateComponents(TaxCode code, decimal amount, int decimals, out decimal lineTax)
    {
        var componentTaxes = new List<(TaxRate Rate, decimal Tax)>();
        lineTax = 0m;

        foreach (var component in code.Components.OrderBy(c => c.Order))
        {
            var rate = component.Rate;
            if (rate == null || !rate.IsActive)
                throw new BusinessException($"Tax rate for code '{code.Name}' is missing or inactive.");

            var baseAmount = component.IsCompound ? amount + lineTax : amount;
            var tax = Math.Round(baseAmount * rate.Rate / 100m, decimals, MidpointRounding.AwayFromZero);
            lineTax += tax;
            componentTaxes.Add((rate, tax));
        }

        return componentTaxes;
    }

    /// <summary>
    /// 把覆盖额按正常口径组件税额的比例分摊到各组件（行级舍入），舍入尾差归最后一个组件，
    /// 保证组件合计恰等于覆盖额。正常口径合计为 0（如零税率）无比例可依时整额归最后一个组件
    /// </summary>
    private static void ApplyOverride(List<(TaxRate Rate, decimal Tax)> componentTaxes, decimal overrideAmount, decimal computedTotal, int decimals)
    {
        var allocated = 0m;
        for (var i = 0; i < componentTaxes.Count; i++)
        {
            // 剩余预算：override 非负（已校验）、正常口径税额非负，故份额恒在 [0, remaining]。
            var remaining = overrideAmount - allocated;
            decimal share;
            if (i == componentTaxes.Count - 1)
            {
                share = remaining;
            }
            else if (computedTotal == 0m)
            {
                share = 0m;
            }
            else
            {
                share = Math.Round(overrideAmount * componentTaxes[i].Tax / computedTotal, decimals, MidpointRounding.AwayFromZero);
                // 3+ 组件时中间份额逐个上舍入会累积超分，钳到剩余预算，
                // 否则最后一个组件的 remainder 变负（组件份额恒非负 + 合计恰等于覆盖额）。
                if (share > remaining) share = remaining;
            }
            allocated += share;
            componentTaxes[i] = (componentTaxes[i].Rate, share);
        }
    }
}
