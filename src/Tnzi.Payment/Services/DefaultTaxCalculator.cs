namespace Tnzi.Payment.Services;

/// <summary>
/// 默认税额计算器：按 <c>Payment:Tax</c> 配置的单一税率计算。
/// </summary>
/// <remarks>
/// 覆盖"全站统一税率"这一最常见场景（如单一辖区经营）。
/// 多辖区、跨境、B2B 反向征收等规则由应用注册自己的 <see cref="ITaxCalculator"/> 承接，
/// 框架不内置任何国家的税表内容。
/// </remarks>
public class DefaultTaxCalculator : ITaxCalculator
{
    private readonly IOptionsMonitor<TaxOptions> _taxOptions;

    public DefaultTaxCalculator(IOptionsMonitor<TaxOptions> taxOptions)
    {
        _taxOptions = Check.NotNull(taxOptions);
    }

    public Task<Result<TaxCalculationResult>> CalculateAsync(TaxCalculationRequest request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        var options = _taxOptions.CurrentValue;
        var netAmount = request.NetAmount;

        if (!options.Enabled || options.DefaultTaxRate <= 0 || netAmount <= 0)
        {
            return Task.FromResult(Result.Success(new TaxCalculationResult
            {
                TaxAmount = 0,
                PayableAmount = Math.Max(0, netAmount),
                TaxRate = 0,
                TaxIncluded = options.TaxIncluded
            }));
        }

        var rate = options.DefaultTaxRate;
        decimal taxAmount;
        decimal payableAmount;

        if (options.TaxIncluded)
        {
            // 价内税：标价即应付额，税额从中反算出来（用于开票列示），不额外加价
            var taxBase = netAmount / (1 + rate / 100m);
            taxAmount = CurrencyInfo.Round(netAmount - taxBase, request.Currency);
            payableAmount = CurrencyInfo.Round(netAmount, request.Currency);
        }
        else
        {
            taxAmount = CurrencyInfo.Round(netAmount * rate / 100m, request.Currency);
            payableAmount = CurrencyInfo.Round(netAmount + taxAmount, request.Currency);
        }

        return Task.FromResult(Result.Success(new TaxCalculationResult
        {
            TaxAmount = taxAmount,
            PayableAmount = payableAmount,
            TaxRate = rate,
            TaxIncluded = options.TaxIncluded
        }));
    }
}
