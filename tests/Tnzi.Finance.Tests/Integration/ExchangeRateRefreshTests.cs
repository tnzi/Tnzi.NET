namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 汇率外部提供者批量刷新：新增 + 覆盖更新 + 无效报价跳过 + 重复元组后者胜(单次 SaveChanges 批量路径)
/// </summary>
public class ExchangeRateRefreshTests : FinanceIntegrationTestBase
{
    private readonly StubExchangeRateProvider _provider = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddSingleton<IExchangeRateProvider>(_provider);
    }

    [Fact]
    public async Task Refresh_MixedQuotes_InsertsUpdatesSkipsAndDedupes()
    {
        // 预置一条既有汇率(将被刷新覆盖)
        var seeded = await InScopeAsync<IExchangeRateService, Result<ExchangeRateDto>>(s => s.UpsertAsync(new UpsertExchangeRateDto
        {
            FromCurrency = "EUR",
            ToCurrency = "USD",
            Rate = 1.05m,
            RateDate = new DateTime(2026, 5, 1)
        }));
        seeded.Succeeded.ShouldBeTrue(seeded.Message);

        _provider.Quotes.AddRange(
        [
            new ExchangeRateQuote("EUR", "USD", 1.10m, new DateTime(2026, 5, 1)),   // 覆盖既有
            new ExchangeRateQuote("GBP", "USD", 1.30m, new DateTime(2026, 5, 1)),   // 新增(将被下面重复元组覆盖)
            new ExchangeRateQuote("GBP", "USD", 1.31m, new DateTime(2026, 5, 1)),   // 重复元组,后者胜
            new ExchangeRateQuote("JPY", "JPY", 1.00m, new DateTime(2026, 5, 1)),   // 无效:同币种
            new ExchangeRateQuote("CAD", "USD", 0m, new DateTime(2026, 5, 1)),      // 无效:零汇率
            new ExchangeRateQuote("chf", "usd", 1.12m, new DateTime(2026, 5, 2))    // 新增(小写归一化)
        ]);

        var result = await InScopeAsync<IExchangeRateService, Result<int>>(s => s.RefreshFromProviderAsync());

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data.ShouldBe(3); // EUR 覆盖 + GBP 去重后一条 + CHF 新增

        var list = await InScopeAsync<IExchangeRateService, Result<IPagedList<ExchangeRateDto>>>(
            s => s.GetListAsync(new ExchangeRateQueryDto()));
        list.Data!.TotalCount.ShouldBe(3);

        var eur = list.Data.Items.Single(r => r.FromCurrency == "EUR");
        eur.Id.ShouldBe(seeded.Data!.Id); // 原地更新,不产生重复行
        eur.Rate.ShouldBe(1.10m);
        eur.Source.ShouldBe(nameof(StubExchangeRateProvider));

        list.Data.Items.Single(r => r.FromCurrency == "GBP").Rate.ShouldBe(1.31m);
        list.Data.Items.Single(r => r.FromCurrency == "CHF").Rate.ShouldBe(1.12m);
    }

    [Fact]
    public async Task Refresh_AllQuotesInvalid_ReturnsZero()
    {
        _provider.Quotes.Add(new ExchangeRateQuote("", "USD", 1.1m, new DateTime(2026, 5, 1)));

        var result = await InScopeAsync<IExchangeRateService, Result<int>>(s => s.RefreshFromProviderAsync());

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data.ShouldBe(0);
    }

    private sealed class StubExchangeRateProvider : IExchangeRateProvider
    {
        public List<ExchangeRateQuote> Quotes { get; } = [];

        public Task<IReadOnlyList<ExchangeRateQuote>> GetLatestRatesAsync(string baseCurrency, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ExchangeRateQuote>>(Quotes);
    }
}
