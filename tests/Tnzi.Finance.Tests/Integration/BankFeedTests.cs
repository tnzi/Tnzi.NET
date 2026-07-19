namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// P3 块 1：银行流水导入（OFX/CSV 解析 + 去重）与匹配（引擎建议 / 确认生成对账勾选行 / 撤销 / 排除 / 批次）
/// </summary>
public class BankFeedTests : FinanceIntegrationTestBase
{
    private const string Ofx2xXml =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <?OFX OFXHEADER="200" VERSION="211" SECURITY="NONE" OLDFILEUID="NONE" NEWFILEUID="NONE"?>
        <OFX><BANKMSGSRSV1><STMTTRNRS><STMTRS>
        <CURDEF>USD</CURDEF>
        <BANKTRANLIST>
        <DTSTART>20260301</DTSTART><DTEND>20260331</DTEND>
        <STMTTRN><TRNTYPE>CREDIT</TRNTYPE><DTPOSTED>20260305120000</DTPOSTED><TRNAMT>500.00</TRNAMT><FITID>FIT-001</FITID><NAME>ACME Corp</NAME><MEMO>Invoice payment</MEMO></STMTTRN>
        <STMTTRN><TRNTYPE>DEBIT</TRNTYPE><DTPOSTED>20260310</DTPOSTED><TRNAMT>-100.00</TRNAMT><FITID>FIT-002</FITID><NAME>Utility</NAME><CHECKNUM>1001</CHECKNUM></STMTTRN>
        </BANKTRANLIST>
        <LEDGERBAL><BALAMT>400.00</BALAMT><DTASOF>20260331</DTASOF></LEDGERBAL>
        </STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>
        """;

    private const string Ofx1xSgml =
        """
        OFXHEADER:100
        DATA:OFXSGML
        VERSION:102
        SECURITY:NONE

        <OFX><BANKMSGSRSV1><STMTTRNRS><STMTRS>
        <CURDEF>USD
        <BANKTRANLIST>
        <DTSTART>20260301
        <DTEND>20260331
        <STMTTRN><TRNTYPE>CREDIT<DTPOSTED>20260305<TRNAMT>250.00<FITID>SGML-1<NAME>Client A<MEMO>Deposit</STMTTRN>
        <STMTTRN><TRNTYPE>DEBIT<DTPOSTED>20260312<TRNAMT>-75.50<FITID>SGML-2<NAME>Vendor B</STMTTRN>
        </BANKTRANLIST>
        <LEDGERBAL><BALAMT>174.50<DTASOF>20260331
        </STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>
        """;

    private static CsvMappingDto SingleColumnMapping() => new()
    {
        HasHeader = true,
        Delimiter = ",",
        DateColumn = 0,
        DateFormat = "yyyy-MM-dd",
        AmountColumn = 2,
        DescriptionColumn = 1
    };

    private async Task<Guid> BankAsync() => await AccountIdByCodeAsync("1120");

    private Task<Result<BankImportResultDto>> ImportOfxAsync(Guid account, string content)
        => InScopeAsync<IBankFeedService, Result<BankImportResultDto>>(
            s => s.ImportStatementAsync(account, BankTransactionSource.Ofx, "test.ofx", content, null));

    private Task<Result<BankImportResultDto>> ImportCsvAsync(Guid account, string content, CsvMappingDto mapping)
        => InScopeAsync<IBankFeedService, Result<BankImportResultDto>>(
            s => s.ImportStatementAsync(account, BankTransactionSource.Csv, "test.csv", content, mapping));

    private async Task<List<BankTransaction>> TxnsAsync(Guid account)
    {
        var repo = ServiceProvider.GetRequiredService<IRepository<BankTransaction, Guid>>();
        return await repo.ToListAsync(t => t.AccountId == account);
    }

    private async Task<BankTransaction> ReloadTxnAsync(Guid id) => (await ReloadAsync<BankTransaction>(id))!;

    /// <summary>过账一笔银行分录（净额 &gt; 0 = 存入借记；&lt; 0 = 支出贷记），返回凭证号</summary>
    private async Task<string> SeedBankLineAsync(Guid bank, decimal net, DateTime date)
    {
        var req = new LedgerPostingRequest
        {
            PostingDate = date,
            SourceType = "Test.Bank",
            SourceId = Guid.NewGuid().ToString("N"),
            Lines = net >= 0
                ?
                [
                    new LedgerPostingLine { AccountId = bank, Debit = net },
                    new LedgerPostingLine { AccountCode = "3100", Credit = net }
                ]
                :
                [
                    new LedgerPostingLine { AccountId = bank, Credit = -net },
                    new LedgerPostingLine { AccountCode = "3100", Debit = -net }
                ]
        };
        var posted = await PostLedgerAsync(req);
        posted.Succeeded.ShouldBeTrue(posted.Message);
        return posted.Data!.Number!;
    }

    private async Task<Guid> BankLineIdAsync(Guid bank, decimal net)
    {
        var repo = ServiceProvider.GetRequiredService<IRepository<JournalLine, Guid>>();
        var line = await repo.FirstOrDefaultAsync(l => l.AccountId == bank && (l.Debit - l.Credit) == net);
        line.ShouldNotBeNull();
        return line.Id;
    }

    private Task<Result<ReconciliationDto>> CreateDraftReconAsync(Guid account, decimal ending, DateTime date)
        => InScopeAsync<IReconciliationService, Result<ReconciliationDto>>(
            s => s.CreateDraftAsync(new CreateReconciliationDto { AccountId = account, StatementDate = date, StatementEndingBalance = ending }));

    private Task<Result<BankSuggestResultDto>> SuggestAsync(Guid account)
        => InScopeAsync<IBankFeedService, Result<BankSuggestResultDto>>(s => s.SuggestMatchesAsync(account));

    private Task<Result<BankTransactionDto>> ConfirmAsync(Guid txnId, Guid? line = null)
        => InScopeAsync<IBankFeedService, Result<BankTransactionDto>>(s => s.ConfirmMatchAsync(txnId, new ConfirmBankMatchDto { JournalLineId = line }));

    // ---- 解析与去重 ----

    /// <summary>回归（B9）：OFX CURDEF 与目标账户币种不符时拒绝导入（防外币对账单导进本位币账户后对着本位币 GL 清算）。</summary>
    [Fact]
    public async Task Ofx_CurrencyMismatch_Rejected()
    {
        await SeedCoaAsync();
        var bank = await BankAsync(); // 1120 = 本位币 USD 资金科目
        const string cadOfx =
            "OFXHEADER:100\n<OFX><BANKMSGSRSV1><STMTTRNRS><STMTRS><CURDEF>CAD</CURDEF>" +
            "<BANKTRANLIST><STMTTRN><DTPOSTED>20260701<TRNAMT>100.00<FITID>X1</STMTTRN></BANKTRANLIST>" +
            "<LEDGERBAL><BALAMT>100.00</LEDGERBAL></STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>";
        var result = await ImportOfxAsync(bank, cadOfx);
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Ofx2xXml_Imports()
    {
        await SeedCoaAsync();
        var bank = await BankAsync();

        var result = await ImportOfxAsync(bank, Ofx2xXml);
        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.ImportedCount.ShouldBe(2);

        var txns = await TxnsAsync(bank);
        txns.Count.ShouldBe(2);
        txns.ShouldContain(t => t.ExternalId == "FIT-001" && t.Amount == 500.00m && t.Payee == "ACME Corp");
        txns.ShouldContain(t => t.ExternalId == "FIT-002" && t.Amount == -100.00m && t.Reference == "1001");
    }

    [Fact]
    public async Task Ofx1xSgml_Imports()
    {
        await SeedCoaAsync();
        var bank = await BankAsync();

        var result = await ImportOfxAsync(bank, Ofx1xSgml);
        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.ImportedCount.ShouldBe(2);

        var txns = await TxnsAsync(bank);
        txns.ShouldContain(t => t.ExternalId == "SGML-1" && t.Amount == 250.00m);
        txns.ShouldContain(t => t.ExternalId == "SGML-2" && t.Amount == -75.50m);
    }

    [Fact]
    public async Task Csv_SingleSignedColumn_Imports()
    {
        await SeedCoaAsync();
        var bank = await BankAsync();
        var csv = "Date,Description,Amount\n2026-03-05,ACME deposit,500.00\n2026-03-10,Utility bill,-100.00\n";

        var result = await ImportCsvAsync(bank, csv, SingleColumnMapping());
        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.ImportedCount.ShouldBe(2);

        var txns = await TxnsAsync(bank);
        txns.ShouldContain(t => t.Amount == 500.00m);
        txns.ShouldContain(t => t.Amount == -100.00m);
    }

    [Fact]
    public async Task Csv_DebitCreditColumns_Imports()
    {
        await SeedCoaAsync();
        var bank = await BankAsync();
        var csv = "Date,Description,Debit,Credit\n2026-03-05,Deposit,,500.00\n2026-03-10,Payment,100.00,\n";
        var mapping = new CsvMappingDto
        {
            HasHeader = true, DateColumn = 0, DescriptionColumn = 1, DebitColumn = 2, CreditColumn = 3, DateFormat = "yyyy-MM-dd"
        };

        var result = await ImportCsvAsync(bank, csv, mapping);
        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.ImportedCount.ShouldBe(2);

        var txns = await TxnsAsync(bank);
        txns.ShouldContain(t => t.Amount == 500.00m);  // credit - debit
        txns.ShouldContain(t => t.Amount == -100.00m);
    }

    [Fact]
    public async Task Ofx_DuplicateFitid_SkippedOnReimport()
    {
        await SeedCoaAsync();
        var bank = await BankAsync();

        (await ImportOfxAsync(bank, Ofx2xXml)).Data!.ImportedCount.ShouldBe(2);
        var second = await ImportOfxAsync(bank, Ofx2xXml);
        second.Data!.ImportedCount.ShouldBe(0);
        second.Data.SkippedCount.ShouldBe(2);
        (await TxnsAsync(bank)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Csv_SameDayAmountName_NotFalselyDeduped_AndCrossFileAligned()
    {
        await SeedCoaAsync();
        var bank = await BankAsync();
        var csv = "Date,Description,Amount\n2026-03-05,Coffee,-5.00\n2026-03-05,Coffee,-5.00\n";

        var first = await ImportCsvAsync(bank, csv, SingleColumnMapping());
        first.Data!.ImportedCount.ShouldBe(2); // 同日同额同名两笔按序号区分，不误杀

        var second = await ImportCsvAsync(bank, csv, SingleColumnMapping());
        second.Data!.ImportedCount.ShouldBe(0); // 跨文件按序号对齐，全部去重
        second.Data.SkippedCount.ShouldBe(2);
    }

    // ---- 匹配规则 ----

    [Fact]
    public async Task Match_Rule1_ExactReference()
    {
        await SeedCoaAsync();
        var bank = await BankAsync();
        var number = await SeedBankLineAsync(bank, 500m, new DateTime(2026, 3, 5));

        var csv = $"Date,Description,Amount,Ref\n2026-03-05,Payment,500.00,{number}\n";
        var mapping = new CsvMappingDto { HasHeader = true, DateColumn = 0, DescriptionColumn = 1, AmountColumn = 2, ReferenceColumn = 3, DateFormat = "yyyy-MM-dd" };
        (await ImportCsvAsync(bank, csv, mapping)).Succeeded.ShouldBeTrue();

        var suggest = await SuggestAsync(bank);
        suggest.Succeeded.ShouldBeTrue(suggest.Message);
        suggest.Data!.Suggested.ShouldBe(1);

        var txn = (await TxnsAsync(bank)).Single();
        txn.SuggestedJournalLineId.ShouldNotBeNull();
        txn.MatchRule.ShouldBe("exact-ref");
        txn.MatchConfidence.ShouldBe(1.0m);
    }

    [Fact]
    public async Task Match_Rule2_AmountDate_UniqueCandidate()
    {
        await SeedCoaAsync();
        var bank = await BankAsync();
        await SeedBankLineAsync(bank, 300m, new DateTime(2026, 3, 6));

        var csv = "Date,Description,Amount\n2026-03-08,Deposit,300.00\n";
        (await ImportCsvAsync(bank, csv, SingleColumnMapping())).Succeeded.ShouldBeTrue();

        var suggest = await SuggestAsync(bank);
        suggest.Data!.Suggested.ShouldBe(1);

        var txn = (await TxnsAsync(bank)).Single();
        txn.MatchRule.ShouldBe("amount-date");
        txn.MatchConfidence.ShouldBe(0.8m);
    }

    [Fact]
    public async Task Match_MultipleCandidates_NoSuggestion_CandidatesEndpointListsAll()
    {
        await SeedCoaAsync();
        var bank = await BankAsync();
        await SeedBankLineAsync(bank, 200m, new DateTime(2026, 3, 4));
        await SeedBankLineAsync(bank, 200m, new DateTime(2026, 3, 6));

        var csv = "Date,Description,Amount\n2026-03-05,Deposit,200.00\n";
        (await ImportCsvAsync(bank, csv, SingleColumnMapping())).Succeeded.ShouldBeTrue();

        var suggest = await SuggestAsync(bank);
        suggest.Data!.Suggested.ShouldBe(0); // 多候选不建议

        var txn = (await TxnsAsync(bank)).Single();
        txn.SuggestedJournalLineId.ShouldBeNull();

        var candidates = await InScopeAsync<IBankFeedService, Result<List<BankMatchCandidateDto>>>(s => s.GetCandidatesAsync(txn.Id));
        candidates.Data!.Count.ShouldBe(2);
    }

    // ---- 确认 / 撤销 / 排除 ----

    [Fact]
    public async Task Confirm_GeneratesReconciliationLine_WorksheetReflects()
    {
        await SeedCoaAsync();
        var bank = await BankAsync();
        await SeedBankLineAsync(bank, 500m, new DateTime(2026, 3, 5));
        var csv = "Date,Description,Amount\n2026-03-05,Deposit,500.00\n";
        await ImportCsvAsync(bank, csv, SingleColumnMapping());
        await SuggestAsync(bank);

        var recon = await CreateDraftReconAsync(bank, 500m, new DateTime(2026, 3, 31));
        recon.Succeeded.ShouldBeTrue(recon.Message);

        var txn = (await TxnsAsync(bank)).Single();
        var confirmed = await ConfirmAsync(txn.Id);
        confirmed.Succeeded.ShouldBeTrue(confirmed.Message);
        confirmed.Data!.Status.ShouldBe(BankTransactionStatus.Matched);
        confirmed.Data.ReconciliationLineId.ShouldNotBeNull();

        var worksheet = await InScopeAsync<IReconciliationService, Result<ReconciliationWorksheetDto>>(s => s.GetWorksheetAsync(recon.Data!.Id));
        worksheet.Data!.ClearedBalance.ShouldBe(500m);
        worksheet.Data.Lines.ShouldContain(l => l.IsSelected);
    }

    [Fact]
    public async Task Worksheet_FlagsLinesHeldByAStatementRow()
    {
        await SeedCoaAsync();
        var bank = await BankAsync();
        await SeedBankLineAsync(bank, 500m, new DateTime(2026, 3, 5));
        await SeedBankLineAsync(bank, 300m, new DateTime(2026, 3, 6));
        await ImportCsvAsync(bank, "Date,Description,Amount\n2026-03-05,Deposit,500.00\n", SingleColumnMapping());
        await SuggestAsync(bank);
        var recon = await CreateDraftReconAsync(bank, 500m, new DateTime(2026, 3, 31));
        var txn = (await TxnsAsync(bank)).Single();
        (await ConfirmAsync(txn.Id)).Succeeded.ShouldBeTrue();

        var worksheet = await InScopeAsync<IReconciliationService, Result<ReconciliationWorksheetDto>>(
            s => s.GetWorksheetAsync(recon.Data!.Id));

        // 呈现端据此禁用勾选框：500 那行被流水持有，300 那行是普通候选
        var matched = await BankLineIdAsync(bank, 500m);
        var other = await BankLineIdAsync(bank, 300m);
        worksheet.Data!.Lines.Single(l => l.JournalLineId == matched).IsStatementMatched.ShouldBeTrue();
        worksheet.Data.Lines.Single(l => l.JournalLineId == other).IsStatementMatched.ShouldBeFalse();
    }

    [Fact]
    public async Task SetLines_DroppingLineHeldByStatement_Rejects409_AndLeavesTransactionIntact()
    {
        await SeedCoaAsync();
        var bank = await BankAsync();
        await SeedBankLineAsync(bank, 500m, new DateTime(2026, 3, 5));
        await ImportCsvAsync(bank, "Date,Description,Amount\n2026-03-05,Deposit,500.00\n", SingleColumnMapping());
        await SuggestAsync(bank);
        var recon = await CreateDraftReconAsync(bank, 500m, new DateTime(2026, 3, 31));
        var txn = (await TxnsAsync(bank)).Single();
        (await ConfirmAsync(txn.Id)).Succeeded.ShouldBeTrue();
        var reconLineId = (await ReloadTxnAsync(txn.Id)).ReconciliationLineId;
        reconLineId.ShouldNotBeNull();

        // 工作区全量替换为空选择 → 会删掉那条流水正持有的勾选行
        var set = await InScopeAsync<IReconciliationService, Result<ReconciliationWorksheetDto>>(
            s => s.SetLinesAsync(recon.Data!.Id, new SetReconciliationLinesDto { JournalLineIds = [] }));

        set.Succeeded.ShouldBeFalse();
        set.Code.ShouldBe(409);
        set.Message.ShouldNotBeNull();
        set.Message.ShouldContain("bank feed");

        // 流水与勾选行都原封不动 —— 否则流水就成了指向不存在勾选行的孤儿
        var reloaded = await ReloadTxnAsync(txn.Id);
        reloaded.Status.ShouldBe(BankTransactionStatus.Matched);
        reloaded.ReconciliationLineId.ShouldBe(reconLineId);
        (await ReloadAsync<ReconciliationLine>(reconLineId.Value)).ShouldNotBeNull();
    }

    [Fact]
    public async Task SetLines_KeepingLineHeldByStatement_StillClearsOthers()
    {
        // 守卫只挡"丢弃被持有的行"，不得妨碍在其之上继续勾选别的行
        await SeedCoaAsync();
        var bank = await BankAsync();
        await SeedBankLineAsync(bank, 500m, new DateTime(2026, 3, 5));
        await SeedBankLineAsync(bank, 300m, new DateTime(2026, 3, 6));
        await ImportCsvAsync(bank, "Date,Description,Amount\n2026-03-05,Deposit,500.00\n", SingleColumnMapping());
        await SuggestAsync(bank);
        var recon = await CreateDraftReconAsync(bank, 800m, new DateTime(2026, 3, 31));
        var txn = (await TxnsAsync(bank)).Single();
        (await ConfirmAsync(txn.Id)).Succeeded.ShouldBeTrue();

        var matched = await BankLineIdAsync(bank, 500m);
        var other = await BankLineIdAsync(bank, 300m);
        var set = await InScopeAsync<IReconciliationService, Result<ReconciliationWorksheetDto>>(
            s => s.SetLinesAsync(recon.Data!.Id, new SetReconciliationLinesDto { JournalLineIds = [matched, other] }));

        set.Succeeded.ShouldBeTrue(set.Message);
        set.Data!.ClearedBalance.ShouldBe(800m);
        set.Data.Difference.ShouldBe(0m);
        (await ReloadTxnAsync(txn.Id)).Status.ShouldBe(BankTransactionStatus.Matched);
    }

    [Fact]
    public async Task Confirm_WithoutDraftReconciliation_Rejects400()
    {
        await SeedCoaAsync();
        var bank = await BankAsync();
        await SeedBankLineAsync(bank, 500m, new DateTime(2026, 3, 5));
        await ImportCsvAsync(bank, "Date,Description,Amount\n2026-03-05,Deposit,500.00\n", SingleColumnMapping());
        await SuggestAsync(bank);

        var txn = (await TxnsAsync(bank)).Single();
        var confirmed = await ConfirmAsync(txn.Id);
        confirmed.Succeeded.ShouldBeFalse();
        confirmed.Code.ShouldBe(400);
        confirmed.Message.ShouldContain("draft reconciliation");
    }

    [Fact]
    public async Task Confirm_ClearedLine_RejectedAsInvalidCandidate()
    {
        await SeedCoaAsync();
        var bank = await BankAsync();
        await SeedBankLineAsync(bank, 500m, new DateTime(2026, 3, 5));
        var lineId = await BankLineIdAsync(bank, 500m);

        // 另一张对账已勾选该总账行
        var recon = await CreateDraftReconAsync(bank, 500m, new DateTime(2026, 3, 31));
        var set = await InScopeAsync<IReconciliationService, Result<ReconciliationWorksheetDto>>(
            s => s.SetLinesAsync(recon.Data!.Id, new SetReconciliationLinesDto { JournalLineIds = new List<Guid> { lineId } }));
        set.Succeeded.ShouldBeTrue(set.Message);

        await ImportCsvAsync(bank, "Date,Description,Amount\n2026-03-05,Deposit,500.00\n", SingleColumnMapping());
        var suggest = await SuggestAsync(bank);
        suggest.Data!.Suggested.ShouldBe(0); // 行已 cleared，不再是候选

        var txn = (await TxnsAsync(bank)).Single();
        var candidates = await InScopeAsync<IBankFeedService, Result<List<BankMatchCandidateDto>>>(s => s.GetCandidatesAsync(txn.Id));
        candidates.Data!.Count.ShouldBe(0);

        var confirmed = await ConfirmAsync(txn.Id, lineId);
        confirmed.Succeeded.ShouldBeFalse();
        confirmed.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Unmatch_ReturnsToPending_RemovesReconciliationLine()
    {
        await SeedCoaAsync();
        var bank = await BankAsync();
        await SeedBankLineAsync(bank, 500m, new DateTime(2026, 3, 5));
        await ImportCsvAsync(bank, "Date,Description,Amount\n2026-03-05,Deposit,500.00\n", SingleColumnMapping());
        await SuggestAsync(bank);
        var recon = await CreateDraftReconAsync(bank, 500m, new DateTime(2026, 3, 31));
        var txn = (await TxnsAsync(bank)).Single();
        await ConfirmAsync(txn.Id);

        var unmatched = await InScopeAsync<IBankFeedService, Result<BankTransactionDto>>(s => s.UnmatchAsync(txn.Id));
        unmatched.Succeeded.ShouldBeTrue(unmatched.Message);
        unmatched.Data!.Status.ShouldBe(BankTransactionStatus.Pending);
        unmatched.Data.ReconciliationLineId.ShouldBeNull();

        var worksheet = await InScopeAsync<IReconciliationService, Result<ReconciliationWorksheetDto>>(s => s.GetWorksheetAsync(recon.Data!.Id));
        worksheet.Data!.ClearedBalance.ShouldBe(0m);
    }

    [Fact]
    public async Task Unmatch_CompletedReconciliation_Rejects409()
    {
        await SeedCoaAsync();
        var bank = await BankAsync();
        await SeedBankLineAsync(bank, 500m, new DateTime(2026, 3, 5));
        await ImportCsvAsync(bank, "Date,Description,Amount\n2026-03-05,Deposit,500.00\n", SingleColumnMapping());
        await SuggestAsync(bank);
        var recon = await CreateDraftReconAsync(bank, 500m, new DateTime(2026, 3, 31));
        var txn = (await TxnsAsync(bank)).Single();
        await ConfirmAsync(txn.Id);

        var completed = await InScopeAsync<IReconciliationService, Result<ReconciliationDto>>(s => s.CompleteAsync(recon.Data!.Id));
        completed.Succeeded.ShouldBeTrue(completed.Message);

        var unmatched = await InScopeAsync<IBankFeedService, Result<BankTransactionDto>>(s => s.UnmatchAsync(txn.Id));
        unmatched.Succeeded.ShouldBeFalse();
        unmatched.Code.ShouldBe(409);
    }

    [Fact]
    public async Task Exclude_Restore_Roundtrip()
    {
        await SeedCoaAsync();
        var bank = await BankAsync();
        await ImportCsvAsync(bank, "Date,Description,Amount\n2026-03-05,Noise,-1.00\n", SingleColumnMapping());
        var txn = (await TxnsAsync(bank)).Single();

        var excluded = await InScopeAsync<IBankFeedService, Result<BankTransactionDto>>(s => s.ExcludeAsync(txn.Id));
        excluded.Data!.Status.ShouldBe(BankTransactionStatus.Excluded);

        var restored = await InScopeAsync<IBankFeedService, Result<BankTransactionDto>>(s => s.RestoreAsync(txn.Id));
        restored.Data!.Status.ShouldBe(BankTransactionStatus.Pending);
    }

    [Fact]
    public async Task CreateDocument_Expense_LinksDraftBack()
    {
        await SeedCoaAsync();
        var bank = await BankAsync();
        var expenseAccount = await AccountIdByCodeAsync("5200");
        await ImportCsvAsync(bank, "Date,Description,Amount\n2026-03-10,Office supplies,-120.00\n", SingleColumnMapping());
        var txn = (await TxnsAsync(bank)).Single();

        var doc = await InScopeAsync<IBankFeedService, Result<BankDocumentResultDto>>(
            s => s.CreateDocumentAsync(txn.Id, new CreateBankDocumentDto { DocType = BankFeedDocType.Expense, CounterAccountId = expenseAccount, PaymentMethod = "Check" }));
        doc.Succeeded.ShouldBeTrue(doc.Message);
        doc.Data!.DocType.ShouldBe("Expense");

        var reloaded = await ReloadTxnAsync(txn.Id);
        reloaded.CreatedDocType.ShouldBe("Expense");
        reloaded.CreatedDocId.ShouldBe(doc.Data.DocId);
    }

    // ---- 批次 ----

    [Fact]
    public async Task DeleteBatch_WithMatched_Rejects409()
    {
        await SeedCoaAsync();
        var bank = await BankAsync();
        await SeedBankLineAsync(bank, 500m, new DateTime(2026, 3, 5));
        var import = await ImportCsvAsync(bank, "Date,Description,Amount\n2026-03-05,Deposit,500.00\n", SingleColumnMapping());
        await SuggestAsync(bank);
        await CreateDraftReconAsync(bank, 500m, new DateTime(2026, 3, 31));
        var txn = (await TxnsAsync(bank)).Single();
        await ConfirmAsync(txn.Id);

        var deleted = await InScopeAsync<IBankFeedService, Result>(s => s.DeleteBatchAsync(import.Data!.BatchId));
        deleted.Succeeded.ShouldBeFalse();
        deleted.Code.ShouldBe(409);
    }

    [Fact]
    public async Task DeleteBatch_NoMatched_SoftDeletesRows()
    {
        await SeedCoaAsync();
        var bank = await BankAsync();
        var import = await ImportCsvAsync(bank, "Date,Description,Amount\n2026-03-05,A,10.00\n2026-03-06,B,20.00\n", SingleColumnMapping());
        (await TxnsAsync(bank)).Count.ShouldBe(2);

        var deleted = await InScopeAsync<IBankFeedService, Result>(s => s.DeleteBatchAsync(import.Data!.BatchId));
        deleted.Succeeded.ShouldBeTrue(deleted.Message);
        (await TxnsAsync(bank)).Count.ShouldBe(0); // 软删后过滤器排除
    }

    [Fact]
    public async Task ForeignCurrencyAccount_SuggestRejected()
    {
        await SeedCoaAsync();
        var acctRepo = ServiceProvider.GetRequiredService<IRepository<Account, Guid>>();
        var eur = new Account
        {
            Code = "1199", Name = "EUR Bank", RootType = AccountRootType.Asset,
            CashFlowActivity = CashFlowActivity.CashEquivalent, Currency = "EUR", IsActive = true, IsGroup = false
        };
        await acctRepo.InsertAsync(eur);
        await acctRepo.SaveChangesAsync();

        (await ImportCsvAsync(eur.Id, "Date,Description,Amount\n2026-03-05,Deposit,100.00\n", SingleColumnMapping())).Succeeded.ShouldBeTrue();

        var suggest = await SuggestAsync(eur.Id);
        suggest.Succeeded.ShouldBeFalse();
        suggest.Code.ShouldBe(400);
    }
}

/// <summary>auto-confirm 开启：精确匹配 + 存在 Draft 对账时自动确认</summary>
public class BankFeedAutoConfirmTests : FinanceIntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.Configure<FinanceOptions>(o => o.BankFeedAutoConfirmExactMatches = true);
    }

    [Fact]
    public async Task Suggest_AutoConfirmsExactMatch_WhenDraftExists()
    {
        await SeedCoaAsync();
        var bank = await AccountIdByCodeAsync("1120");

        var req = new LedgerPostingRequest
        {
            PostingDate = new DateTime(2026, 3, 5),
            SourceType = "Test.Bank",
            SourceId = Guid.NewGuid().ToString("N"),
            Lines =
            [
                new LedgerPostingLine { AccountId = bank, Debit = 500m },
                new LedgerPostingLine { AccountCode = "3100", Credit = 500m }
            ]
        };
        var posted = await InScopeAsync<ILedgerPostingService, Result<JournalEntryDto>>(s => s.PostAsync(req));
        posted.Succeeded.ShouldBeTrue(posted.Message);
        var number = posted.Data!.Number!;

        var csv = $"Date,Description,Amount,Ref\n2026-03-05,Payment,500.00,{number}\n";
        var mapping = new CsvMappingDto { HasHeader = true, DateColumn = 0, DescriptionColumn = 1, AmountColumn = 2, ReferenceColumn = 3, DateFormat = "yyyy-MM-dd" };
        await InScopeAsync<IBankFeedService, Result<BankImportResultDto>>(s => s.ImportStatementAsync(bank, BankTransactionSource.Csv, "f.csv", csv, mapping));

        await InScopeAsync<IReconciliationService, Result<ReconciliationDto>>(
            s => s.CreateDraftAsync(new CreateReconciliationDto { AccountId = bank, StatementDate = new DateTime(2026, 3, 31), StatementEndingBalance = 500m }));

        var suggest = await InScopeAsync<IBankFeedService, Result<BankSuggestResultDto>>(s => s.SuggestMatchesAsync(bank));
        suggest.Succeeded.ShouldBeTrue(suggest.Message);
        suggest.Data!.AutoConfirmed.ShouldBe(1);

        var repo = ServiceProvider.GetRequiredService<IRepository<BankTransaction, Guid>>();
        var txn = (await repo.ToListAsync(t => t.AccountId == bank)).Single();
        txn.Status.ShouldBe(BankTransactionStatus.Matched);
    }
}

/// <summary>BankImportMaxRows 超限整批拒绝</summary>
public class BankFeedMaxRowsTests : FinanceIntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.Configure<FinanceOptions>(o => o.BankImportMaxRows = 2);
    }

    [Fact]
    public async Task Import_ExceedingMaxRows_Rejected400()
    {
        await SeedCoaAsync();
        var bank = await AccountIdByCodeAsync("1120");
        var csv = "Date,Description,Amount\n2026-03-01,A,1.00\n2026-03-02,B,2.00\n2026-03-03,C,3.00\n";
        var mapping = new CsvMappingDto { HasHeader = true, DateColumn = 0, DescriptionColumn = 1, AmountColumn = 2, DateFormat = "yyyy-MM-dd" };

        var result = await InScopeAsync<IBankFeedService, Result<BankImportResultDto>>(
            s => s.ImportStatementAsync(bank, BankTransactionSource.Csv, "big.csv", csv, mapping));
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }
}
