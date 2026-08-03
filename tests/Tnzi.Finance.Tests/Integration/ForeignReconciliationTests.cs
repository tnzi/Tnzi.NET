namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 外币对账（口径切换）+ 多币种连贯场景。对账币种 = 科目限定币种 ?? 本位币；
/// 外币限定科目走交易币口径（候选只取本币行、金额投影 Txn、cleared=ΣTxn、完成门交易币精确 0），
/// 本位币/不限币科目走本位币口径（现状零变化）。
/// </summary>
public class ForeignReconciliationTests : FinanceIntegrationTestBase
{
    private async Task<Guid> CreateFundsAccountAsync(string code, string? currency, string parentCode = "1100")
    {
        var parent = await AccountIdByCodeAsync(parentCode);
        return await CreateAccountAsync(new CreateAccountDto
        {
            Code = code,
            Name = currency == null ? "Wallet" : $"{currency} Wallet",
            RootType = AccountRootType.Asset,
            Currency = currency,
            ParentId = parent,
            SubType = "Bank",
            CashFlowActivity = CashFlowActivity.CashEquivalent
        });
    }

    private async Task PostFxAsync(DateTime date, string currency, decimal rate, string sourceId, params LedgerPostingLine[] lines)
    {
        var result = await PostLedgerAsync(Posting(date, currency, rate, sourceId, lines));
        result.Succeeded.ShouldBeTrue(result.Message);
    }

    private Task<Result<ReconciliationDto>> CreateReconAsync(Guid account, decimal endingBalance, DateTime date)
        => InScopeAsync<IReconciliationService, Result<ReconciliationDto>>(s => s.CreateDraftAsync(new CreateReconciliationDto
        {
            AccountId = account,
            StatementDate = date,
            StatementEndingBalance = endingBalance
        }));

    [Fact]
    public async Task ForeignReconciliation_EurFullFlow_UsesTransactionCurrencyCaliber()
    {
        await SeedCoaAsync();
        var eur = await CreateFundsAccountAsync("1121", "EUR");
        await UpsertRateAsync("EUR", "USD", 1.10m, new DateTime(2026, 1, 1));

        // EUR 交易币余额 = 1000 + 500 - 300 = 1200（本位币价值随汇率变，但对账走交易币）
        await PostFxAsync(new DateTime(2026, 3, 5), "EUR", 1.10m, "eur-1",
            new LedgerPostingLine { AccountId = eur, Debit = 1000m }, new LedgerPostingLine { AccountCode = "3100", Credit = 1000m });
        await PostFxAsync(new DateTime(2026, 3, 10), "EUR", 1.10m, "eur-2",
            new LedgerPostingLine { AccountId = eur, Debit = 500m }, new LedgerPostingLine { AccountCode = "3100", Credit = 500m });
        await PostFxAsync(new DateTime(2026, 3, 15), "EUR", 1.10m, "eur-3",
            new LedgerPostingLine { AccountCode = "5200", Debit = 300m }, new LedgerPostingLine { AccountId = eur, Credit = 300m });

        var draft = await CreateReconAsync(eur, 1200m, new DateTime(2026, 3, 31));
        draft.Succeeded.ShouldBeTrue(draft.Message);
        draft.Data!.Currency.ShouldBe("EUR");
        draft.Data.Difference.ShouldBe(1200m);

        var worksheet = await InScopeAsync<IReconciliationService, Result<ReconciliationWorksheetDto>>(
            s => s.GetWorksheetAsync(draft.Data.Id));
        worksheet.Data!.Currency.ShouldBe("EUR");
        worksheet.Data.Lines.Count.ShouldBe(3);
        // 金额投影为交易币（EUR）口径
        worksheet.Data.Lines.Single(l => l.Debit == 1000m).ShouldNotBeNull();
        worksheet.Data.Lines.Single(l => l.Credit == 300m).ShouldNotBeNull();

        var cleared = await InScopeAsync<IReconciliationService, Result<ReconciliationWorksheetDto>>(
            s => s.SetLinesAsync(draft.Data.Id, new SetReconciliationLinesDto
            {
                JournalLineIds = worksheet.Data.Lines.Select(l => l.JournalLineId).ToList()
            }));
        cleared.Data!.ClearedBalance.ShouldBe(1200m);
        cleared.Data.Difference.ShouldBe(0m);

        var completed = await InScopeAsync<IReconciliationService, Result<ReconciliationDto>>(
            s => s.CompleteAsync(draft.Data.Id));
        completed.Succeeded.ShouldBeTrue(completed.Message);
        completed.Data!.Status.ShouldBe(ReconciliationStatus.Completed);
    }

    [Fact]
    public async Task ForeignReconciliation_ExcludesRevaluationAdjustmentRows()
    {
        await SeedCoaAsync();
        var eur = await CreateFundsAccountAsync("1121", "EUR");
        await UpsertRateAsync("EUR", "USD", 1.10m, new DateTime(2026, 1, 1));
        await PostFxAsync(new DateTime(2026, 3, 5), "EUR", 1.10m, "eur-1",
            new LedgerPostingLine { AccountId = eur, Debit = 1000m }, new LedgerPostingLine { AccountCode = "3100", Credit = 1000m });

        // 重估在 EUR 科目上落一本位币（USD）调整行
        await UpsertRateAsync("EUR", "USD", 1.20m, new DateTime(2026, 3, 31));
        var run = await InScopeAsync<IRevaluationService, Result<RevaluationPreviewDto>>(
            s => s.RunAsync(new RunRevaluationDto { AsOf = new DateTime(2026, 3, 31) }));
        run.Succeeded.ShouldBeTrue(run.Message);
        run.Data!.JournalEntryId.ShouldNotBeNull();

        var draft = await CreateReconAsync(eur, 1000m, new DateTime(2026, 3, 31));
        draft.Succeeded.ShouldBeTrue(draft.Message);

        // 候选只有 1 条 EUR 行；本位币重估调整行被天然排除
        var worksheet = await InScopeAsync<IReconciliationService, Result<ReconciliationWorksheetDto>>(
            s => s.GetWorksheetAsync(draft.Data!.Id));
        worksheet.Data!.Lines.Count.ShouldBe(1);
        worksheet.Data.Lines[0].Debit.ShouldBe(1000m);

        // 勾选 EUR 行 → cleared 1000 EUR（交易币口径，不含 +200 本位币调整）→ 差额 0 可完成
        var cleared = await InScopeAsync<IReconciliationService, Result<ReconciliationWorksheetDto>>(
            s => s.SetLinesAsync(draft.Data!.Id, new SetReconciliationLinesDto
            {
                JournalLineIds = worksheet.Data.Lines.Select(l => l.JournalLineId).ToList()
            }));
        cleared.Data!.ClearedBalance.ShouldBe(1000m);
        cleared.Data.Difference.ShouldBe(0m);
        (await InScopeAsync<IReconciliationService, Result<ReconciliationDto>>(s => s.CompleteAsync(draft.Data!.Id)))
            .Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task ForeignReconciliation_CrossCurrencyTransferLine_EntersCandidates()
    {
        await SeedCoaAsync();
        var usdBank = await AccountIdByCodeAsync("1120");
        var eur = await CreateFundsAccountAsync("1121", "EUR");

        // 跨币种划转 USD 1100 → EUR 1000 @1.10：EUR 侧行以 EUR 进候选
        var draft = await InScopeAsync<ITransferService, Result<TransferDto>>(s => s.CreateDraftAsync(new CreateTransferDto
        {
            FromAccountId = usdBank,
            ToAccountId = eur,
            TransferDate = new DateTime(2026, 3, 10),
            Currency = "USD",
            Amount = 1100m,
            TargetCurrency = "EUR",
            TargetAmount = 1000m,
            TargetExchangeRate = 1.10m
        }));
        var posted = await InScopeAsync<ITransferService, Result<TransferDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);

        var recon = await CreateReconAsync(eur, 1000m, new DateTime(2026, 3, 31));
        recon.Succeeded.ShouldBeTrue(recon.Message);
        recon.Data!.Currency.ShouldBe("EUR");

        var worksheet = await InScopeAsync<IReconciliationService, Result<ReconciliationWorksheetDto>>(
            s => s.GetWorksheetAsync(recon.Data.Id));
        worksheet.Data!.Lines.Count.ShouldBe(1);
        worksheet.Data.Lines[0].Debit.ShouldBe(1000m); // EUR 交易币金额

        var cleared = await InScopeAsync<IReconciliationService, Result<ReconciliationWorksheetDto>>(
            s => s.SetLinesAsync(recon.Data.Id, new SetReconciliationLinesDto
            {
                JournalLineIds = worksheet.Data.Lines.Select(l => l.JournalLineId).ToList()
            }));
        cleared.Data!.Difference.ShouldBe(0m);
    }

    [Fact]
    public async Task BaseCurrencyRestrictedReconciliation_UsesBaseCaliber()
    {
        await SeedCoaAsync();
        var usd = await CreateFundsAccountAsync("1121", "USD"); // 本位币限定
        await PostFxAsync(new DateTime(2026, 3, 5), "USD", 1m, "usd-1",
            new LedgerPostingLine { AccountId = usd, Debit = 700m }, new LedgerPostingLine { AccountCode = "3100", Credit = 700m });

        var draft = await CreateReconAsync(usd, 700m, new DateTime(2026, 3, 31));
        draft.Succeeded.ShouldBeTrue(draft.Message);
        draft.Data!.Currency.ShouldBe("USD");

        var worksheet = await InScopeAsync<IReconciliationService, Result<ReconciliationWorksheetDto>>(
            s => s.GetWorksheetAsync(draft.Data.Id));
        worksheet.Data!.Lines.Count.ShouldBe(1);
        worksheet.Data.Lines[0].Debit.ShouldBe(700m);

        var cleared = await InScopeAsync<IReconciliationService, Result<ReconciliationWorksheetDto>>(
            s => s.SetLinesAsync(draft.Data.Id, new SetReconciliationLinesDto
            {
                JournalLineIds = worksheet.Data.Lines.Select(l => l.JournalLineId).ToList()
            }));
        cleared.Data!.Difference.ShouldBe(0m);
        (await InScopeAsync<IReconciliationService, Result<ReconciliationDto>>(s => s.CompleteAsync(draft.Data.Id)))
            .Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task UnrestrictedAccountReconciliation_DerivesBaseCurrency()
    {
        await SeedCoaAsync();
        var bank = await AccountIdByCodeAsync("1120"); // 不限币（Currency = null）
        await PostFxAsync(new DateTime(2026, 3, 5), "USD", 1m, "u-1",
            new LedgerPostingLine { AccountId = bank, Debit = 400m }, new LedgerPostingLine { AccountCode = "3100", Credit = 400m });

        var draft = await CreateReconAsync(bank, 400m, new DateTime(2026, 3, 31));
        draft.Succeeded.ShouldBeTrue(draft.Message);
        draft.Data!.Currency.ShouldBe("USD"); // 派生本位币

        var worksheet = await InScopeAsync<IReconciliationService, Result<ReconciliationWorksheetDto>>(
            s => s.GetWorksheetAsync(draft.Data.Id));
        worksheet.Data!.Currency.ShouldBe("USD");
        worksheet.Data.Lines.Count.ShouldBe(1);
        worksheet.Data.Lines[0].Debit.ShouldBe(400m);
    }

    [Fact]
    public async Task MultiCurrency_Coherence_AllThreeStatementIdentitiesStayZero()
    {
        await SeedCoaAsync();
        var usdBank = await AccountIdByCodeAsync("1120");
        var eur = await CreateFundsAccountAsync("1121", "EUR");
        await UpsertRateAsync("EUR", "USD", 1.10m, new DateTime(2026, 1, 1));

        // ① EUR 收入（Dr EUR 银行 / Cr 4100 销售收入，EUR 交易币）
        await PostFxAsync(new DateTime(2026, 3, 1), "EUR", 1.10m, "sale",
            new LedgerPostingLine { AccountId = eur, Debit = 2000m }, new LedgerPostingLine { AccountCode = "4100", Credit = 2000m });

        // ② 跨币种划转 USD 550 → EUR 500 @1.10（residual 0）
        var transfer = await InScopeAsync<ITransferService, Result<TransferDto>>(s => s.CreateDraftAsync(new CreateTransferDto
        {
            FromAccountId = usdBank,
            ToAccountId = eur,
            TransferDate = new DateTime(2026, 3, 5),
            Currency = "USD",
            Amount = 550m,
            TargetCurrency = "EUR",
            TargetAmount = 500m,
            TargetExchangeRate = 1.10m
        }));
        (await InScopeAsync<ITransferService, Result<TransferDto>>(s => s.PostAsync(transfer.Data!.Id))).Succeeded.ShouldBeTrue();

        // ③ 期末重估 @1.25
        await UpsertRateAsync("EUR", "USD", 1.25m, new DateTime(2026, 3, 31));
        (await InScopeAsync<IRevaluationService, Result<RevaluationPreviewDto>>(
            s => s.RunAsync(new RunRevaluationDto { AsOf = new DateTime(2026, 3, 31) }))).Succeeded.ShouldBeTrue();

        // ④ EUR 科目对账（交易币余额 = 2000 + 500 = 2500）
        var recon = await CreateReconAsync(eur, 2500m, new DateTime(2026, 3, 31));
        recon.Succeeded.ShouldBeTrue(recon.Message);
        var ws = await InScopeAsync<IReconciliationService, Result<ReconciliationWorksheetDto>>(
            s => s.GetWorksheetAsync(recon.Data!.Id));
        await InScopeAsync<IReconciliationService, Result<ReconciliationWorksheetDto>>(
            s => s.SetLinesAsync(recon.Data!.Id, new SetReconciliationLinesDto
            {
                JournalLineIds = ws.Data!.Lines.Select(l => l.JournalLineId).ToList()
            }));
        (await InScopeAsync<IReconciliationService, Result<ReconciliationDto>>(s => s.CompleteAsync(recon.Data!.Id)))
            .Succeeded.ShouldBeTrue();

        // ⑤ 三表恒等式全 0
        var from = new DateTime(2026, 3, 1);
        var to = new DateTime(2026, 3, 31);
        var tb = await InScopeAsync<IFinancialReportService, Result<TrialBalanceReportDto>>(s => s.GetTrialBalanceAsync(from, to));
        tb.Data!.TotalPeriodDebit.ShouldBe(tb.Data.TotalPeriodCredit);
        tb.Data.TotalClosingBalance.ShouldBe(0m);

        var bs = await InScopeAsync<IFinancialReportService, Result<BalanceSheetReportDto>>(s => s.GetBalanceSheetAsync(to));
        bs.Data!.BalanceCheck.ShouldBe(0m);

        var cf = await InScopeAsync<IFinancialReportService, Result<CashFlowReportDto>>(s => s.GetCashFlowAsync(from, to));
        cf.Data!.CheckDifference.ShouldBe(0m);
    }
}
