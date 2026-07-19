namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 跨币种换汇划转（路线 C）：三张单币凭证经换汇过渡科目归零，residual 记汇兑损益。
/// 覆盖全周期定格 + 过渡归零 / residual=0 两张 / 三币三张 / 校验矩阵 /
/// 作废全冲销 / 引擎放宽回归 / 冲销链完整。
/// </summary>
public class CrossCurrencyTransferTests : FinanceIntegrationTestBase
{
    private async Task<Guid> CreateFundsAccountAsync(string code, string currency)
    {
        var parent = await AccountIdByCodeAsync("1100");
        return await CreateAccountAsync(new CreateAccountDto
        {
            Code = code,
            Name = $"{currency} Wallet",
            RootType = AccountRootType.Asset,
            Currency = currency,
            ParentId = parent,
            SubType = "Bank",
            CashFlowActivity = CashFlowActivity.CashEquivalent
        });
    }

    private Task<Result<TransferDto>> CreateCrossAsync(
        Guid from, string currency, decimal amount, decimal? rate,
        Guid to, string targetCurrency, decimal targetAmount, decimal? targetRate,
        DateTime date)
        => InScopeAsync<ITransferService, Result<TransferDto>>(s => s.CreateDraftAsync(new CreateTransferDto
        {
            FromAccountId = from,
            ToAccountId = to,
            TransferDate = date,
            Currency = currency,
            Amount = amount,
            ExchangeRate = rate,
            TargetCurrency = targetCurrency,
            TargetAmount = targetAmount,
            TargetExchangeRate = targetRate
        }));

    private async Task<decimal> ClosingBalanceAsync(Guid accountId, DateTime from, DateTime to)
    {
        var gl = await InScopeAsync<IFinancialReportService, Result<GeneralLedgerReportDto>>(
            s => s.GetGeneralLedgerAsync(accountId, from, to, new PagedQueryDto { PageIndex = 1, PageSize = 50 }));
        gl.Succeeded.ShouldBeTrue(gl.Message);
        return gl.Data!.ClosingBalance;
    }

    [Fact]
    public async Task CrossTransfer_FullLifecycle_CapturesRatesAndNetsClearing()
    {
        await SeedCoaAsync();
        var usdBank = await AccountIdByCodeAsync("1120"); // 不限币，接受 USD
        var eur = await CreateFundsAccountAsync("1121", "EUR");
        var clearing = await AccountIdByCodeAsync("1900");
        var date = new DateTime(2026, 3, 10);

        // 转出 USD 1080 → 转入 EUR 1000 @1.10（base_in 1100 > base_out 1080 → 汇兑收益 20）
        var draft = await CreateCrossAsync(usdBank, "USD", 1080m, null, eur, "EUR", 1000m, 1.10m, date);
        draft.Succeeded.ShouldBeTrue(draft.Message);
        draft.Data!.TargetCurrency.ShouldBe("EUR");
        draft.Data.TargetAmount.ShouldBe(1000m);

        var posted = await InScopeAsync<ITransferService, Result<TransferDto>>(s => s.PostAsync(draft.Data.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);
        posted.Data!.Status.ShouldBe(FinanceDocumentStatus.Posted);
        posted.Data.Number.ShouldBe("TRF-000001");
        posted.Data.BaseAmount.ShouldBe(1080m);        // 凭证1（转出币）
        posted.Data.TargetBaseAmount.ShouldBe(1100m);  // 凭证2（转入币）
        posted.Data.TargetExchangeRate.ShouldBe(1.10m);
        posted.Data.JournalEntryId.ShouldNotBeNull();
        posted.Data.TargetJournalEntryId.ShouldNotBeNull();
        posted.Data.FxJournalEntryId.ShouldNotBeNull(); // residual != 0

        // 过渡科目在同工作单元内精确归零
        (await ClosingBalanceAsync(clearing, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31))).ShouldBe(0m);

        // 转入 EUR 科目：交易币借 1000 EUR，本位币借 1100
        var inEntry = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
            s => s.GetAsync(posted.Data.TargetJournalEntryId!.Value));
        var eurLine = inEntry.Data!.Lines.Single(l => l.AccountId == eur);
        eurLine.TxnDebit.ShouldBe(1000m);
        eurLine.Debit.ShouldBe(1100m);
    }

    [Fact]
    public async Task CrossTransfer_ZeroResidual_PostsOnlyTwoVouchers()
    {
        await SeedCoaAsync();
        var usdBank = await AccountIdByCodeAsync("1120");
        var eur = await CreateFundsAccountAsync("1121", "EUR");
        var date = new DateTime(2026, 3, 10);

        // USD 1100 → EUR 1000 @1.10：base_out 1100 == base_in 1100 → 无 residual，2 张凭证
        var draft = await CreateCrossAsync(usdBank, "USD", 1100m, null, eur, "EUR", 1000m, 1.10m, date);
        draft.Succeeded.ShouldBeTrue(draft.Message);

        var posted = await InScopeAsync<ITransferService, Result<TransferDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);
        posted.Data!.JournalEntryId.ShouldNotBeNull();
        posted.Data.TargetJournalEntryId.ShouldNotBeNull();
        posted.Data.FxJournalEntryId.ShouldBeNull(); // residual == 0

        var vouchers = await InScopeAsync<ILedgerPostingService, Result<List<JournalEntryDto>>>(
            s => s.GetBySourceAsync(nameof(Transfer), posted.Data.Id.ToString()));
        vouchers.Data!.Count.ShouldBe(2);
    }

    [Fact]
    public async Task CrossTransfer_ThreeCurrencies_PostsThreeVouchers()
    {
        await SeedCoaAsync();
        var eur = await CreateFundsAccountAsync("1121", "EUR");
        var gbp = await CreateFundsAccountAsync("1122", "GBP");
        var clearing = await AccountIdByCodeAsync("1900");
        var date = new DateTime(2026, 3, 10);

        // EUR 1000 @1.20 (base_out 1200) → GBP 800 @1.40 (base_in 1120) → residual 80（损失）
        var draft = await CreateCrossAsync(eur, "EUR", 1000m, 1.20m, gbp, "GBP", 800m, 1.40m, date);
        draft.Succeeded.ShouldBeTrue(draft.Message);

        var posted = await InScopeAsync<ITransferService, Result<TransferDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);
        posted.Data!.BaseAmount.ShouldBe(1200m);
        posted.Data.TargetBaseAmount.ShouldBe(1120m);
        posted.Data.FxJournalEntryId.ShouldNotBeNull();

        var vouchers = await InScopeAsync<ILedgerPostingService, Result<List<JournalEntryDto>>>(
            s => s.GetBySourceAsync(nameof(Transfer), posted.Data.Id.ToString()));
        vouchers.Data!.Count.ShouldBe(3);
        vouchers.Data.Select(v => v.Currency).Distinct().OrderBy(c => c).ShouldBe(new[] { "EUR", "GBP", "USD" });

        // 汇兑损益（5800）借方 80（损失）
        var fx = await AccountIdByCodeAsync("5800");
        var fxEntry = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
            s => s.GetAsync(posted.Data.FxJournalEntryId!.Value));
        fxEntry.Data!.Lines.Single(l => l.AccountId == fx).Debit.ShouldBe(80m);

        (await ClosingBalanceAsync(clearing, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31))).ShouldBe(0m);
    }

    [Fact]
    public async Task CrossTransfer_ValidationMatrix_EnforcesModeInvariants()
    {
        await SeedCoaAsync();
        var usdBank = await AccountIdByCodeAsync("1120");
        var eur = await CreateFundsAccountAsync("1121", "EUR");
        var date = new DateTime(2026, 3, 10);

        // 同币种模式传 Target* → 400（完全后向兼容）
        var usdWallet = await CreateFundsAccountAsync("1131", "USD");
        var sameWithTarget = await InScopeAsync<ITransferService, Result<TransferDto>>(s => s.CreateDraftAsync(new CreateTransferDto
        {
            FromAccountId = usdBank,
            ToAccountId = usdWallet,
            TransferDate = date,
            Currency = "USD",
            Amount = 100m,
            TargetAmount = 100m // 同币种模式非法
        }));
        sameWithTarget.Succeeded.ShouldBeFalse();
        sameWithTarget.Code.ShouldBe(400);

        // 跨币种模式缺 TargetAmount → 400
        var crossNoTarget = await InScopeAsync<ITransferService, Result<TransferDto>>(s => s.CreateDraftAsync(new CreateTransferDto
        {
            FromAccountId = usdBank,
            ToAccountId = eur,
            TransferDate = date,
            Currency = "USD",
            Amount = 100m,
            TargetCurrency = "EUR"
        }));
        crossNoTarget.Succeeded.ShouldBeFalse();
        crossNoTarget.Code.ShouldBe(400);

        // 合法跨币种草稿成功
        var ok = await CreateCrossAsync(usdBank, "USD", 100m, null, eur, "EUR", 90m, 1.11m, date);
        ok.Succeeded.ShouldBeTrue(ok.Message);
    }

    [Fact]
    public async Task CrossTransfer_Void_ReversesAllVouchers_AndNetsToZero()
    {
        await SeedCoaAsync();
        var eur = await CreateFundsAccountAsync("1121", "EUR");
        var gbp = await CreateFundsAccountAsync("1122", "GBP");
        var clearing = await AccountIdByCodeAsync("1900");
        var fx = await AccountIdByCodeAsync("5800");
        var date = new DateTime(2026, 3, 10);
        var from = new DateTime(2026, 3, 1);
        var to = new DateTime(2026, 3, 31);

        var draft = await CreateCrossAsync(eur, "EUR", 1000m, 1.20m, gbp, "GBP", 800m, 1.40m, date);
        var posted = await InScopeAsync<ITransferService, Result<TransferDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);

        var voided = await InScopeAsync<ITransferService, Result<TransferDto>>(s => s.VoidAsync(posted.Data!.Id));
        voided.Succeeded.ShouldBeTrue(voided.Message);
        voided.Data!.Status.ShouldBe(FinanceDocumentStatus.Voided);
        voided.Data.VoidJournalEntryId.ShouldNotBeNull();

        // 全部相关科目净额归零
        (await ClosingBalanceAsync(eur, from, to)).ShouldBe(0m);
        (await ClosingBalanceAsync(gbp, from, to)).ShouldBe(0m);
        (await ClosingBalanceAsync(clearing, from, to)).ShouldBe(0m);
        (await ClosingBalanceAsync(fx, from, to)).ShouldBe(0m);
    }

    [Fact]
    public async Task Engine_Relaxation_AcceptsBaseCurrencyLine_RejectsThirdCurrency()
    {
        await SeedCoaAsync();
        var eur = await CreateFundsAccountAsync("1121", "EUR");

        // 回归 A：本位币行落在外币限定科目上被接受（realized/unrealized FX 调整语义）
        var baseLine = await PostLedgerAsync(Posting(new DateTime(2026, 3, 15), "USD", 1m, "adj-usd",
            new LedgerPostingLine { AccountId = eur, Debit = 50m },
            new LedgerPostingLine { AccountCode = "5800", Credit = 50m }));
        baseLine.Succeeded.ShouldBeTrue(baseLine.Message);

        // 回归 B：第三种币（GBP）落在 EUR 限定科目上被拒（只接受 EUR 或 USD）
        var thirdCurrency = await PostLedgerAsync(Posting(new DateTime(2026, 3, 16), "GBP", 1.30m, "gbp-bad",
            new LedgerPostingLine { AccountId = eur, Debit = 50m },
            new LedgerPostingLine { AccountCode = "3100", Credit = 50m }));
        thirdCurrency.Succeeded.ShouldBeFalse();
        thirdCurrency.Code.ShouldBe(400);
    }

    [Fact]
    public async Task CrossTransfer_Void_LinksReversalChain()
    {
        await SeedCoaAsync();
        var eur = await CreateFundsAccountAsync("1121", "EUR");
        var gbp = await CreateFundsAccountAsync("1122", "GBP");
        var date = new DateTime(2026, 3, 10);

        var draft = await CreateCrossAsync(eur, "EUR", 1000m, 1.20m, gbp, "GBP", 800m, 1.40m, date);
        var posted = await InScopeAsync<ITransferService, Result<TransferDto>>(s => s.PostAsync(draft.Data!.Id));
        var sourceId = posted.Data!.Id.ToString();

        await InScopeAsync<ITransferService, Result<TransferDto>>(s => s.VoidAsync(posted.Data.Id));

        var all = await InScopeAsync<ILedgerPostingService, Result<List<JournalEntryDto>>>(
            s => s.GetBySourceAsync(nameof(Transfer), sourceId));
        // 3 原始 + 3 冲销 = 6 张凭证
        all.Data!.Count.ShouldBe(6);
        all.Data.Count(v => v.Status == JournalEntryStatus.Reversed).ShouldBe(3);
        all.Data.Count(v => v.ReversalOfEntryId != null).ShouldBe(3);
        // 每张原始凭证都有 ReversedByEntryId 指向其冲销
        all.Data.Where(v => v.ReversalOfEntryId == null).ShouldAllBe(v => v.ReversedByEntryId != null);
    }
}
