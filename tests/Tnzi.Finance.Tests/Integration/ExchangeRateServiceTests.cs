namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 汇率：幂等 Upsert、最近日期解析、反向汇率、外部提供者缺失
/// </summary>
public class ExchangeRateServiceTests : FinanceIntegrationTestBase
{
    private Task<Result<ExchangeRateDto>> UpsertAsync(string from, string to, decimal rate, DateTime date)
        => InScopeAsync<IExchangeRateService, Result<ExchangeRateDto>>(s => s.UpsertAsync(new UpsertExchangeRateDto
        {
            FromCurrency = from,
            ToCurrency = to,
            Rate = rate,
            RateDate = date
        }));

    [Fact]
    public async Task Upsert_SamePairAndDate_UpdatesInsteadOfDuplicating()
    {
        var first = await UpsertAsync("EUR", "USD", 1.1m, new DateTime(2026, 1, 1));
        first.Succeeded.ShouldBeTrue(first.Message);

        var second = await UpsertAsync("EUR", "USD", 1.2m, new DateTime(2026, 1, 1));
        second.Succeeded.ShouldBeTrue(second.Message);
        second.Data!.Id.ShouldBe(first.Data!.Id);
        second.Data.Rate.ShouldBe(1.2m);

        var list = await InScopeAsync<IExchangeRateService, Result<IPagedList<ExchangeRateDto>>>(
            s => s.GetListAsync(new ExchangeRateQueryDto()));
        list.Data!.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Upsert_InvalidInput_Fails()
    {
        (await UpsertAsync("EUR", "EUR", 1.0m, new DateTime(2026, 1, 1))).Succeeded.ShouldBeFalse();
        (await UpsertAsync("EUR", "USD", 0m, new DateTime(2026, 1, 1))).Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task Resolve_UsesNearestEarlierRate()
    {
        await UpsertAsync("EUR", "USD", 1.0m, new DateTime(2026, 1, 1));
        await UpsertAsync("EUR", "USD", 1.2m, new DateTime(2026, 3, 1));

        var february = await InScopeAsync<IExchangeRateService, decimal?>(
            s => s.ResolveRateAsync("EUR", "USD", new DateTime(2026, 2, 15)));
        february.ShouldBe(1.0m);

        var march = await InScopeAsync<IExchangeRateService, decimal?>(
            s => s.ResolveRateAsync("EUR", "USD", new DateTime(2026, 3, 2)));
        march.ShouldBe(1.2m);
    }

    [Fact]
    public async Task Resolve_FallsBackToInverseRate()
    {
        await UpsertAsync("USD", "EUR", 0.8m, new DateTime(2026, 1, 1));

        var rate = await InScopeAsync<IExchangeRateService, decimal?>(
            s => s.ResolveRateAsync("EUR", "USD", new DateTime(2026, 2, 1)));

        rate.ShouldBe(1.25m);
    }

    [Fact]
    public async Task Resolve_SameCurrency_ReturnsOne()
    {
        var rate = await InScopeAsync<IExchangeRateService, decimal?>(
            s => s.ResolveRateAsync("USD", "usd", new DateTime(2026, 1, 1)));

        rate.ShouldBe(1m);
    }

    [Fact]
    public async Task Resolve_MissingRate_ReturnsNull()
    {
        var rate = await InScopeAsync<IExchangeRateService, decimal?>(
            s => s.ResolveRateAsync("JPY", "USD", new DateTime(2026, 1, 1)));

        rate.ShouldBeNull();
    }

    [Fact]
    public async Task Refresh_WithoutProvider_Returns501()
    {
        var result = await InScopeAsync<IExchangeRateService, Result<int>>(s => s.RefreshFromProviderAsync());

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(501);
    }
}
