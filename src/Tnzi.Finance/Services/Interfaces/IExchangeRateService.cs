namespace Tnzi.Finance.Services.Interfaces;

/// <summary>
/// 汇率服务
/// </summary>
public interface IExchangeRateService
{
    /// <summary>
    /// 解析汇率：同币种返回 1；优先直接汇率（不晚于目标日期的最近一条），
    /// 其次反向汇率取倒数；无可用汇率返回 null
    /// </summary>
    Task<decimal?> ResolveRateAsync(string fromCurrency, string toCurrency, DateTime date, CancellationToken cancellationToken = default);

    /// <summary>分页查询汇率</summary>
    Task<Result<IPagedList<ExchangeRateDto>>> GetListAsync(ExchangeRateQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>录入/更新汇率（按 币种对 + 日期 幂等）</summary>
    Task<Result<ExchangeRateDto>> UpsertAsync(UpsertExchangeRateDto input, CancellationToken cancellationToken = default);

    /// <summary>删除汇率</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>从外部提供者刷新最新汇率（未注册 IExchangeRateProvider 时返回失败）</summary>
    Task<Result<int>> RefreshFromProviderAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 外部汇率提供者契约（框架不内置实现；应用注册后可用 RefreshFromProviderAsync 拉取）
/// </summary>
public interface IExchangeRateProvider
{
    /// <summary>
    /// 获取以 baseCurrency 为目标币种的最新汇率报价（1 单位报价币 = Rate 单位 baseCurrency）
    /// </summary>
    Task<IReadOnlyList<ExchangeRateQuote>> GetLatestRatesAsync(string baseCurrency, CancellationToken cancellationToken = default);
}

/// <summary>
/// 汇率报价
/// </summary>
public record ExchangeRateQuote(string FromCurrency, string ToCurrency, decimal Rate, DateTime RateDate);
