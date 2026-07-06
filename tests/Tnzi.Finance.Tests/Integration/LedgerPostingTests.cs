namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 总账过账管线：平衡校验、科目校验、多币种换算与舍入配平、连续凭证号、期间锁定、来源反查
/// </summary>
public class LedgerPostingTests : FinanceIntegrationTestBase
{
    [Fact]
    public async Task Post_BalancedEntry_Succeeds()
    {
        await SeedCoaAsync();

        var result = await PostLedgerAsync(SimpleSale(100m, sourceId: "sale-1"));

        result.Succeeded.ShouldBeTrue(result.Message);
        var dto = result.Data!;
        dto.Status.ShouldBe(JournalEntryStatus.Posted);
        dto.Number.ShouldBe("JE-000001");
        dto.TotalDebit.ShouldBe(100m);
        dto.TotalCredit.ShouldBe(100m);
        dto.Currency.ShouldBe("USD");
        dto.ExchangeRate.ShouldBe(1m);
        dto.Lines.Count.ShouldBe(2);
        dto.Lines.Sum(l => l.Debit).ShouldBe(100m);
        dto.Lines.Sum(l => l.Credit).ShouldBe(100m);
    }

    [Fact]
    public async Task Post_UnbalancedEntry_Fails()
    {
        await SeedCoaAsync();

        var request = SimpleSale(100m);
        request.Lines[1].Credit = 90m;

        var result = await PostLedgerAsync(request);

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain("not balanced");
    }

    [Fact]
    public async Task Post_SingleLine_Fails()
    {
        await SeedCoaAsync();

        var request = SimpleSale(100m);
        request.Lines.RemoveAt(1);

        var result = await PostLedgerAsync(request);
        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task Post_LineWithBothSides_Fails()
    {
        await SeedCoaAsync();

        var request = SimpleSale(100m);
        request.Lines[0].Credit = 50m;

        var result = await PostLedgerAsync(request);
        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain("exactly one");
    }

    [Fact]
    public async Task Post_NegativeAmount_Fails()
    {
        await SeedCoaAsync();

        var request = SimpleSale(100m);
        request.Lines[0].Debit = -100m;

        var result = await PostLedgerAsync(request);
        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain("negative");
    }

    [Fact]
    public async Task Post_ToGroupAccount_Fails()
    {
        await SeedCoaAsync();

        var request = SimpleSale(100m);
        request.Lines[0] = new LedgerPostingLine { AccountCode = "1000", Debit = 100m };

        var result = await PostLedgerAsync(request);
        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain("group account");
    }

    [Fact]
    public async Task Post_ToInactiveAccount_Fails()
    {
        await SeedCoaAsync();

        var other = await InScopeAsync<IChartOfAccountsService, Account?>(s => s.FindByCodeAsync("4900"));
        var deactivate = await InScopeAsync<IChartOfAccountsService, Result<AccountDto>>(s => s.UpdateAsync(other!.Id, new UpdateAccountDto
        {
            Code = other.Code,
            Name = other.Name,
            ParentId = other.ParentId,
            IsActive = false
        }));
        deactivate.Succeeded.ShouldBeTrue(deactivate.Message);

        var request = SimpleSale(100m);
        request.Lines[1] = new LedgerPostingLine { AccountCode = "4900", Credit = 100m };

        var result = await PostLedgerAsync(request);
        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain("inactive");
    }

    [Fact]
    public async Task Post_UnresolvableAccount_Fails()
    {
        await SeedCoaAsync();

        var request = SimpleSale(100m);
        request.Lines[0] = new LedgerPostingLine { AccountCode = "does-not-exist", Debit = 100m };

        var result = await PostLedgerAsync(request);
        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain("unable to resolve");
    }

    [Fact]
    public async Task Post_MissingSource_Fails()
    {
        await SeedCoaAsync();

        var request = SimpleSale(100m);
        request.SourceType = "";

        var result = await PostLedgerAsync(request);
        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain("SourceType");
    }

    [Fact]
    public async Task Post_ForeignCurrency_WithoutRate_Fails()
    {
        await SeedCoaAsync();

        var request = SimpleSale(100m);
        request.Currency = "EUR";

        var result = await PostLedgerAsync(request);
        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain("No exchange rate");
    }

    [Fact]
    public async Task Post_ForeignCurrency_ResolvesRateAndConverts()
    {
        await SeedCoaAsync();

        var upsert = await InScopeAsync<IExchangeRateService, Result<ExchangeRateDto>>(s => s.UpsertAsync(new UpsertExchangeRateDto
        {
            FromCurrency = "EUR",
            ToCurrency = "USD",
            Rate = 1.1m,
            RateDate = new DateTime(2026, 1, 1)
        }));
        upsert.Succeeded.ShouldBeTrue(upsert.Message);

        var request = SimpleSale(100m);
        request.Currency = "EUR";

        var result = await PostLedgerAsync(request);

        result.Succeeded.ShouldBeTrue(result.Message);
        var dto = result.Data!;
        dto.ExchangeRate.ShouldBe(1.1m);
        dto.TotalDebit.ShouldBe(110m);
        dto.TotalCredit.ShouldBe(110m);
        dto.Lines[0].TxnDebit.ShouldBe(100m);
        dto.Lines[0].Debit.ShouldBe(110m);
    }

    [Fact]
    public async Task Post_ForeignCurrency_RoundingResidual_AddsRoundingLine()
    {
        await SeedCoaAsync();

        // 汇率 1.115：借方 33.33/33.33/33.34 → 37.16+37.16+37.17=111.49；
        // 贷方 100 → 111.50；尾差 0.01 由舍入差额行自动配平
        var request = new LedgerPostingRequest
        {
            PostingDate = new DateTime(2026, 3, 15),
            Currency = "EUR",
            ExchangeRate = 1.115m,
            SourceType = "Test.Rounding",
            SourceId = "round-1",
            Lines =
            [
                new LedgerPostingLine { AccountRole = AccountSystemRole.AccountsReceivable, Debit = 33.33m },
                new LedgerPostingLine { AccountCode = "1120", Debit = 33.33m },
                new LedgerPostingLine { AccountCode = "1110", Debit = 33.34m },
                new LedgerPostingLine { AccountCode = "4100", Credit = 100m }
            ]
        };

        var result = await PostLedgerAsync(request);

        result.Succeeded.ShouldBeTrue(result.Message);
        var dto = result.Data!;
        dto.Lines.Count.ShouldBe(5);
        dto.TotalDebit.ShouldBe(dto.TotalCredit);
        dto.TotalDebit.ShouldBe(111.50m);

        var roundingLine = dto.Lines.Single(l => l.Memo == "Automatic rounding difference");
        roundingLine.Debit.ShouldBe(0.01m);
        roundingLine.TxnDebit.ShouldBe(0m);
    }

    [Fact]
    public async Task Post_ManyLineForeignCurrency_ToleranceScalesWithLineCount()
    {
        await SeedCoaAsync();

        // 30 借方行 33.33 + 1 贷方行 999.90 @ 1.111111：
        // 逐行合法舍入误差累积到 -0.10，超过固定容差 0.05，
        // 但在按行数缩放的容差内 → 必须过账成功并由舍入差额行配平
        var request = new LedgerPostingRequest
        {
            PostingDate = new DateTime(2026, 3, 15),
            Currency = "EUR",
            ExchangeRate = 1.111111m,
            SourceType = "Test.ToleranceScale",
            SourceId = "tol-1",
            Lines = Enumerable.Range(0, 30)
                .Select(_ => new LedgerPostingLine { AccountRole = AccountSystemRole.AccountsReceivable, Debit = 33.33m })
                .Append(new LedgerPostingLine { AccountCode = "4100", Credit = 999.90m })
                .ToList()
        };

        var result = await PostLedgerAsync(request);

        result.Succeeded.ShouldBeTrue(result.Message);
        var dto = result.Data!;
        dto.TotalDebit.ShouldBe(dto.TotalCredit);

        // 借方 30×37.16=1110.90，贷方 1111.00 → 舍入差额行补借方 0.10
        var roundingLine = dto.Lines.Single(l => l.Memo == "Automatic rounding difference");
        roundingLine.Debit.ShouldBe(0.10m);
    }

    [Fact]
    public async Task Post_Numbers_AreConsecutive_EvenAfterFailedAttempt()
    {
        await SeedCoaAsync();

        var first = await PostLedgerAsync(SimpleSale(100m));
        first.Data!.Number.ShouldBe("JE-000001");

        // 失败的过账（校验先于编号分配）不烧号
        var invalid = SimpleSale(100m);
        invalid.Lines[1].Credit = 1m;
        (await PostLedgerAsync(invalid)).Succeeded.ShouldBeFalse();

        var second = await PostLedgerAsync(SimpleSale(200m));
        second.Data!.Number.ShouldBe("JE-000002");
    }

    [Fact]
    public async Task GetBySource_ReturnsPostedEntry()
    {
        await SeedCoaAsync();
        await PostLedgerAsync(SimpleSale(100m, sourceId: "order-42"));

        var result = await InScopeAsync<ILedgerPostingService, Result<List<JournalEntryDto>>>(
            s => s.GetBySourceAsync("Test.Sale", "order-42"));

        result.Succeeded.ShouldBeTrue();
        result.Data!.Count.ShouldBe(1);
        result.Data[0].Lines.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Post_IntoClosedFiscalYear_Fails_ThenSucceedsAfterReopen()
    {
        await SeedCoaAsync();

        var created = await InScopeAsync<IFiscalYearService, Result<FiscalYearDto>>(s => s.CreateAsync(new CreateFiscalYearDto
        {
            Name = "FY2020",
            StartDate = new DateTime(2020, 1, 1),
            EndDate = new DateTime(2020, 12, 31)
        }));
        created.Succeeded.ShouldBeTrue(created.Message);

        var closed = await InScopeAsync<IFiscalYearService, Result>(s => s.CloseAsync(created.Data!.Id));
        closed.Succeeded.ShouldBeTrue(closed.Message);

        var blocked = await PostLedgerAsync(SimpleSale(100m, new DateTime(2020, 6, 15)));
        blocked.Succeeded.ShouldBeFalse();
        blocked.Code.ShouldBe(409);
        blocked.Message.ShouldNotBeNull();
        blocked.Message.ShouldContain("closed fiscal year");

        var reopened = await InScopeAsync<IFiscalYearService, Result>(s => s.ReopenAsync(created.Data!.Id));
        reopened.Succeeded.ShouldBeTrue(reopened.Message);

        var allowed = await PostLedgerAsync(SimpleSale(100m, new DateTime(2020, 6, 15)));
        allowed.Succeeded.ShouldBeTrue(allowed.Message);
    }
}
