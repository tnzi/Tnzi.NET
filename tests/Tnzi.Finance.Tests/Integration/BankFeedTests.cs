using Tnzi.EventBus;
using Tnzi.Finance.Banking.Events;

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
        confirmed.Message!.ShouldContain("draft reconciliation");
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
        // Draft only: no posting, no match, and the line is still For review.
        doc.Data.Posted.ShouldBeFalse();
        doc.Data.Matched.ShouldBeFalse();
        reloaded.Status.ShouldBe(BankTransactionStatus.Pending);
    }

    [Fact]
    public async Task CreateDocument_PostAndMatch_PostsAndClearsInOneStep()
    {
        await SeedCoaAsync();
        var bank = await BankAsync();
        var expenseAccount = await AccountIdByCodeAsync("5200");
        await ImportCsvAsync(bank, "Date,Description,Amount\n2026-03-10,Office supplies,-120.00\n", SingleColumnMapping());
        await CreateDraftReconAsync(bank, -120m, new DateTime(2026, 3, 31));
        var txn = (await TxnsAsync(bank)).Single();

        var doc = await InScopeAsync<IBankFeedService, Result<BankDocumentResultDto>>(
            s => s.CreateDocumentAsync(txn.Id, new CreateBankDocumentDto
            {
                DocType = BankFeedDocType.Expense,
                CounterAccountId = expenseAccount,
                PaymentMethod = "Check",
                PostAndMatch = true
            }));

        doc.Succeeded.ShouldBeTrue(doc.Message);
        doc.Data!.Posted.ShouldBeTrue();
        doc.Data.Matched.ShouldBeTrue();
        doc.Data.JournalEntryId.ShouldNotBeNull();

        // The reconcile flow only works if one click actually clears the line;
        // stopping at a draft would push the operator out to the expense page.
        var reloaded = await ReloadTxnAsync(txn.Id);
        reloaded.Status.ShouldBe(BankTransactionStatus.Matched);
        reloaded.MatchedJournalLineId.ShouldNotBeNull();
        reloaded.ReconciliationLineId.ShouldNotBeNull();
    }

    [Fact]
    public async Task CreateDocument_PostAndMatch_NoDraftReconciliation_Rejects400_WithoutWriting()
    {
        await SeedCoaAsync();
        var bank = await BankAsync();
        var expenseAccount = await AccountIdByCodeAsync("5200");
        await ImportCsvAsync(bank, "Date,Description,Amount\n2026-03-10,Office supplies,-120.00\n", SingleColumnMapping());
        var txn = (await TxnsAsync(bank)).Single();

        var doc = await InScopeAsync<IBankFeedService, Result<BankDocumentResultDto>>(
            s => s.CreateDocumentAsync(txn.Id, new CreateBankDocumentDto
            {
                DocType = BankFeedDocType.Expense,
                CounterAccountId = expenseAccount,
                PostAndMatch = true
            }));

        doc.Succeeded.ShouldBeFalse();
        doc.Code.ShouldBe(400);

        // The precondition is checked BEFORE anything is created, so a rejected
        // call must not leave an orphan draft expense nobody intends to manage.
        var reloaded = await ReloadTxnAsync(txn.Id);
        reloaded.CreatedDocType.ShouldBeNull();
        reloaded.CreatedDocId.ShouldBeNull();
        reloaded.Status.ShouldBe(BankTransactionStatus.Pending);
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

    // ── 银行规则与匹配引擎的分工（P4-3）────────────────────────

    private Task<Result<BankRuleDto>> CreateRuleAsync(CreateBankRuleDto input)
        => InScopeAsync<IBankRuleService, Result<BankRuleDto>>(s => s.CreateAsync(input));

    /// <summary>
    /// ★规则只在匹配引擎找不到对手方时才参与。
    /// </summary>
    /// <remarks>
    /// 账上已经有那笔钱了还按规则再记一笔，就是重复入账——这条边界是规则功能
    /// 能不能被信任的前提。
    /// </remarks>
    [Fact]
    public async Task Suggest_RuleStandsDown_WhenTheLedgerAlreadyHasTheCounterpart()
    {
        await SeedCoaAsync();
        var bank = await BankAsync();
        var meals = await AccountIdByCodeAsync("5200");

        await CreateRuleAsync(new CreateBankRuleDto
        {
            Name = "Coffee",
            DocType = BankFeedDocType.Expense,
            CounterAccountId = meals,
            Conditions = [new() { Field = BankRuleField.Description, Operator = BankRuleOperator.Contains, Value = "coffee" }],
        });

        // 账上已有一笔完全对得上的支出
        await SeedBankLineAsync(bank, -25m, DateTime.UtcNow.Date);
        var csv = $"Date,Description,Amount\n{DateTime.UtcNow:yyyy-MM-dd},COFFEE SHOP,-25.00\n";
        await ImportCsvAsync(bank, csv, SingleColumnMapping());

        var suggest = await SuggestAsync(bank);

        suggest.Succeeded.ShouldBeTrue(suggest.Message);
        suggest.Data!.Suggested.ShouldBe(1);
        suggest.Data.RuleSuggested.ShouldBe(0);

        var txn = (await TxnsAsync(bank)).Single();
        txn.SuggestedJournalLineId.ShouldNotBeNull();
        txn.SuggestedRuleId.ShouldBeNull();
    }

    [Fact]
    public async Task Suggest_RuleFillsTheGap_WhenTheLedgerHasNothing()
    {
        await SeedCoaAsync();
        var bank = await BankAsync();
        var meals = await AccountIdByCodeAsync("5200");

        var rule = await CreateRuleAsync(new CreateBankRuleDto
        {
            Name = "Coffee",
            DocType = BankFeedDocType.Expense,
            CounterAccountId = meals,
            Conditions = [new() { Field = BankRuleField.Description, Operator = BankRuleOperator.Contains, Value = "coffee" }],
        });

        var csv = $"Date,Description,Amount\n{DateTime.UtcNow:yyyy-MM-dd},COFFEE SHOP,-25.00\n";
        await ImportCsvAsync(bank, csv, SingleColumnMapping());

        var suggest = await SuggestAsync(bank);

        suggest.Data!.Suggested.ShouldBe(0);
        suggest.Data.RuleSuggested.ShouldBe(1);

        var txn = (await TxnsAsync(bank)).Single();
        txn.SuggestedRuleId.ShouldBe(rule.Data!.Id);
        txn.Status.ShouldBe(BankTransactionStatus.Pending);
    }

    /// <summary>
    /// ★AutoApply 走的是「新建并对账」同一条路径：自动入账与手工点一次的结果逐字相同。
    /// </summary>
    [Fact]
    public async Task Suggest_AutoApplyRule_Creates_Posts_AndMatches()
    {
        await SeedCoaAsync();
        var bank = await BankAsync();
        var meals = await AccountIdByCodeAsync("5200");
        await CreateDraftReconAsync(bank, -25m, DateTime.UtcNow.Date);

        await CreateRuleAsync(new CreateBankRuleDto
        {
            Name = "Coffee auto",
            DocType = BankFeedDocType.Expense,
            CounterAccountId = meals,
            AutoApply = true,
            Conditions = [new() { Field = BankRuleField.Description, Operator = BankRuleOperator.Contains, Value = "coffee" }],
        });

        var csv = $"Date,Description,Amount\n{DateTime.UtcNow:yyyy-MM-dd},COFFEE SHOP,-25.00\n";
        await ImportCsvAsync(bank, csv, SingleColumnMapping());

        var suggest = await SuggestAsync(bank);

        suggest.Succeeded.ShouldBeTrue(suggest.Message);
        suggest.Data!.AutoCategorized.ShouldBe(1);

        var txn = (await TxnsAsync(bank)).Single();
        txn.Status.ShouldBe(BankTransactionStatus.Matched);
        txn.CreatedDocType.ShouldNotBeNull();
        txn.ReconciliationLineId.ShouldNotBeNull();
        txn.MatchedJournalLineId.ShouldNotBeNull();
    }

    /// <summary>
    /// AutoApply 规则在没有 Draft 对账时退回普通建议，而不是自作主张地入账。
    /// </summary>
    [Fact]
    public async Task Suggest_AutoApplyRule_FallsBackToASuggestion_WithoutADraftReconciliation()
    {
        await SeedCoaAsync();
        var bank = await BankAsync();
        var meals = await AccountIdByCodeAsync("5200");

        await CreateRuleAsync(new CreateBankRuleDto
        {
            Name = "Coffee auto",
            DocType = BankFeedDocType.Expense,
            CounterAccountId = meals,
            AutoApply = true,
            Conditions = [new() { Field = BankRuleField.Description, Operator = BankRuleOperator.Contains, Value = "coffee" }],
        });

        var csv = $"Date,Description,Amount\n{DateTime.UtcNow:yyyy-MM-dd},COFFEE SHOP,-25.00\n";
        await ImportCsvAsync(bank, csv, SingleColumnMapping());

        var suggest = await SuggestAsync(bank);

        suggest.Data!.AutoCategorized.ShouldBe(0);
        suggest.Data.RuleSuggested.ShouldBe(1);
        (await TxnsAsync(bank)).Single().Status.ShouldBe(BankTransactionStatus.Pending);
    }
}

/// <summary>auto-confirm 开启：精确匹配 + 存在 Draft 对账时自动确认</summary>
/// <remarks>
/// 自动确认分支是 <c>ConfirmMatchAsync</c> 的后半段，必须与它同一口径：轮换父对账的并发戳
/// （与 <c>CompleteAsync</c> 互斥，否则勾选行会插进已完成的对账）并发布匹配事件。
/// 这两条既有用例都看不见：断言 <c>AutoConfirmed == 1</c> 对「漏掉 bump」与「漏掉事件」照样全绿。
/// </remarks>
public class BankFeedAutoConfirmTests : FinanceIntegrationTestBase
{
    private readonly MatchedEventRecorder _events = new();
    private readonly ConcurrentCompleter _completer = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.Configure<FinanceOptions>(o => o.BankFeedAutoConfirmExactMatches = true);
        services.AddSingleton(_events);
        services.AddScoped<IEventHandler<BankTransactionMatchedEvent>, RecordingMatchedHandler>();
        services.AddSingleton(_completer);
        // 后注册者胜：BankFeedService 拿到的是这个能在取候选时「并发完成对账」的子类。
        services.AddScoped<BankMatchEngine, InterleavingBankMatchEngine>();
    }

    private async Task<(Guid Bank, Guid JournalLineId)> SeedExactRefScenarioAsync()
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

        var lines = ServiceProvider.GetRequiredService<IRepository<JournalLine, Guid>>();
        var line = await lines.FirstOrDefaultAsync(l => l.AccountId == bank && l.Debit == 500m);
        line.ShouldNotBeNull();
        return (bank, line.Id);
    }

    private Task<Result<ReconciliationDto>> CreateDraftAsync(Guid bank, decimal ending)
        => InScopeAsync<IReconciliationService, Result<ReconciliationDto>>(
            s => s.CreateDraftAsync(new CreateReconciliationDto { AccountId = bank, StatementDate = new DateTime(2026, 3, 31), StatementEndingBalance = ending }));

    private Task<Result<BankSuggestResultDto>> SuggestAsync(Guid bank)
        => InScopeAsync<IBankFeedService, Result<BankSuggestResultDto>>(s => s.SuggestMatchesAsync(bank));

    [Fact]
    public async Task Suggest_AutoConfirmsExactMatch_WhenDraftExists()
    {
        var (bank, _) = await SeedExactRefScenarioAsync();
        (await CreateDraftAsync(bank, 500m)).Succeeded.ShouldBeTrue();

        var suggest = await SuggestAsync(bank);
        suggest.Succeeded.ShouldBeTrue(suggest.Message);
        suggest.Data!.AutoConfirmed.ShouldBe(1);

        var repo = ServiceProvider.GetRequiredService<IRepository<BankTransaction, Guid>>();
        var txn = (await repo.ToListAsync(t => t.AccountId == bank)).Single();
        txn.Status.ShouldBe(BankTransactionStatus.Matched);
    }

    /// <summary>
    /// 自动确认往 Draft 对账里插了勾选行，父对账的并发戳必须随之轮换 ——
    /// 那是它与 <c>CompleteAsync</c> 互斥的唯一机制（勾选行自己没有并发令牌）。
    /// </summary>
    [Fact]
    public async Task Suggest_AutoConfirm_RotatesTheDraftReconciliationStamp()
    {
        var (bank, _) = await SeedExactRefScenarioAsync();
        var draft = await CreateDraftAsync(bank, 500m);
        var before = (await ReloadAsync<Reconciliation>(draft.Data!.Id))!.ConcurrencyStamp;

        var suggest = await SuggestAsync(bank);
        suggest.Succeeded.ShouldBeTrue(suggest.Message);
        suggest.Data!.AutoConfirmed.ShouldBe(1);

        (await ReloadAsync<Reconciliation>(draft.Data.Id))!.ConcurrencyStamp.ShouldNotBe(before);
    }

    /// <summary>自动确认的匹配与手工确认的匹配对订阅方必须长得一样 —— 否则消费方的联动只对手工那一半生效。</summary>
    [Fact]
    public async Task Suggest_AutoConfirm_PublishesMatchedEvent()
    {
        var (bank, journalLineId) = await SeedExactRefScenarioAsync();
        (await CreateDraftAsync(bank, 500m)).Succeeded.ShouldBeTrue();

        var suggest = await SuggestAsync(bank);
        suggest.Succeeded.ShouldBeTrue(suggest.Message);

        var repo = ServiceProvider.GetRequiredService<IRepository<BankTransaction, Guid>>();
        var txn = (await repo.ToListAsync(t => t.AccountId == bank)).Single();

        var evt = _events.Events.ShouldHaveSingleItem();
        evt.BankTransactionId.ShouldBe(txn.Id);
        evt.AccountId.ShouldBe(bank);
        evt.JournalLineId.ShouldBe(journalLineId);
        evt.ReconciliationLineId.ShouldBe(txn.ReconciliationLineId!.Value);
    }

    /// <summary>
    /// 交错：建议匹配读到 Draft 之后、写入之前，另一个人完成了这张对账。
    /// 勾选行绝不能落进已完成的对账（那一期从此永久对不平且不能重开），整批回滚并 409。
    /// </summary>
    /// <remarks>
    /// 交错由替换进 DI 的引擎子类制造：取候选集那一刻（在 Draft 已被读出、事务尚未开始之间）
    /// 用另一个 scope 完成对账。对账单期末余额取 0 = 完成时的 cleared 余额，让完成本身合法。
    /// </remarks>
    [Fact]
    public async Task Suggest_AutoConfirm_WhenTheDraftIsCompletedMeanwhile_Returns409AndWritesNothing()
    {
        var (bank, _) = await SeedExactRefScenarioAsync();
        var draft = await CreateDraftAsync(bank, 0m);
        _completer.ReconciliationId = draft.Data!.Id;

        var suggest = await SuggestAsync(bank);

        _completer.Completed.ShouldBeTrue("the probe must actually have completed the reconciliation mid-flight");
        suggest.Succeeded.ShouldBeFalse("a line must not be inserted into a reconciliation completed concurrently");
        suggest.Code.ShouldBe(409);

        var lines = ServiceProvider.GetRequiredService<IRepository<ReconciliationLine, Guid>>();
        (await lines.CountAsync(l => l.ReconciliationId == draft.Data.Id)).ShouldBe(0);
        var repo = ServiceProvider.GetRequiredService<IRepository<BankTransaction, Guid>>();
        (await repo.ToListAsync(t => t.AccountId == bank)).Single().Status.ShouldBe(BankTransactionStatus.Pending);
        _events.Events.ShouldBeEmpty();
    }

    private sealed class MatchedEventRecorder
    {
        public List<BankTransactionMatchedEvent> Events { get; } = new();
    }

    private sealed class RecordingMatchedHandler : IEventHandler<BankTransactionMatchedEvent>
    {
        private readonly MatchedEventRecorder _recorder;
        public RecordingMatchedHandler(MatchedEventRecorder recorder) => _recorder = recorder;

        public Task HandleAsync(BankTransactionMatchedEvent @event, CancellationToken cancellationToken = default)
        {
            _recorder.Events.Add(@event);
            return Task.CompletedTask;
        }
    }

    private sealed class ConcurrentCompleter
    {
        public Guid? ReconciliationId { get; set; }
        public bool Completed { get; set; }
    }

    private sealed class InterleavingBankMatchEngine : BankMatchEngine
    {
        private readonly ConcurrentCompleter _completer;
        private readonly IServiceScopeFactory _scopes;

        public InterleavingBankMatchEngine(
            IReadOnlyRepository<JournalLine, Guid> journalLineRepository,
            IReadOnlyRepository<ReconciliationLine, Guid> reconLineRepository,
            IReadOnlyRepository<BankTransaction, Guid> bankTxnRepository,
            IOptionsSnapshot<FinanceOptions> options,
            ConcurrentCompleter completer,
            IServiceScopeFactory scopes)
            : base(journalLineRepository, reconLineRepository, bankTxnRepository, options)
        {
            _completer = completer;
            _scopes = scopes;
        }

        public override async Task<Dictionary<decimal, List<BankMatchCandidate>>> GetCandidatesByAmountAsync(
            Guid accountId, IReadOnlyCollection<decimal> amounts, CancellationToken cancellationToken)
        {
            var candidates = await base.GetCandidatesByAmountAsync(accountId, amounts, cancellationToken);
            if (_completer.ReconciliationId is { } id && !_completer.Completed)
            {
                using var scope = _scopes.CreateScope();
                var completed = await scope.ServiceProvider.GetRequiredService<IReconciliationService>().CompleteAsync(id, cancellationToken);
                completed.Succeeded.ShouldBeTrue(completed.Message);
                _completer.Completed = true;
            }
            return candidates;
        }
    }
}

/// <summary>
/// 流水导入把解析器 / 提供者给的字符串写进定宽列（Description 512 / Payee 256 / Reference 128 / ExternalId 256）
/// 之前必须按列宽归一化。
/// </summary>
/// <remarks>
/// 此前原样赋值：SQL Server / PostgreSQL 上一行超长就在循环中间抛 500，批次头与前 N-1 行已各自提交、
/// 计数停在 0/0、事件没发；重传时前 N-1 行按去重跳过、那一行再炸一次，这份对账单永远导不完。
/// 判据同 <c>ReceiptFieldLimits</c>：机器给的值归一化（文件来自银行，操作员改不了它）；
/// 给人看的字段截断保留开头，标识符不截断 —— 截出来的是另一个键。
/// ⚠️ 测试库是 SQLite，它<b>不执行 varchar 长度约束</b>，所以断言落在「存下来的值已是归一化后的形状」
/// 而不是「没有抛异常」；真实库上未归一化的值会让插入失败，那是本机制存在的动机，在这里测不出来。
/// </remarks>
public class BankFeedFieldLimitsTests : FinanceIntegrationTestBase
{
    private static string Ofx(params string[] stmttrns) =>
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <?OFX OFXHEADER="200" VERSION="211" SECURITY="NONE" OLDFILEUID="NONE" NEWFILEUID="NONE"?>
        <OFX><BANKMSGSRSV1><STMTTRNRS><STMTRS>
        <CURDEF>USD</CURDEF>
        <BANKTRANLIST>
        <DTSTART>20260301</DTSTART><DTEND>20260331</DTEND>
        """ + string.Concat(stmttrns) + """

        </BANKTRANLIST>
        <LEDGERBAL><BALAMT>400.00</BALAMT><DTASOF>20260331</DTASOF></LEDGERBAL>
        </STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>
        """;

    private static string Stmttrn(string fitid, string name, string memo, string? checknum = null, string amount = "10.00")
        => $"<STMTTRN><TRNTYPE>CREDIT</TRNTYPE><DTPOSTED>20260305</DTPOSTED><TRNAMT>{amount}</TRNAMT><FITID>{fitid}</FITID><NAME>{name}</NAME><MEMO>{memo}</MEMO>"
           + (checknum == null ? "" : $"<CHECKNUM>{checknum}</CHECKNUM>") + "</STMTTRN>";

    private Task<Result<BankImportResultDto>> ImportAsync(Guid account, string content)
        => InScopeAsync<IBankFeedService, Result<BankImportResultDto>>(
            s => s.ImportStatementAsync(account, BankTransactionSource.Ofx, "long.ofx", content, null));

    private async Task<List<BankTransaction>> TxnsAsync(Guid account)
    {
        var repo = ServiceProvider.GetRequiredService<IRepository<BankTransaction, Guid>>();
        return await repo.ToListAsync(t => t.AccountId == account);
    }

    [Fact]
    public async Task Import_TruncatesOverlongDescriptionAndPayee_ToTheColumnWidth()
    {
        await SeedCoaAsync();
        var bank = await AccountIdByCodeAsync("1120");
        var memo = new string('m', 600);
        var name = new string('n', 300);

        var result = await ImportAsync(bank, Ofx(Stmttrn("FIT-LONG-1", name, memo)));

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.ImportedCount.ShouldBe(1);
        var txn = (await TxnsAsync(bank)).Single();
        txn.Description!.Length.ShouldBe(BankTransactionFieldLimits.DescriptionMaxLength);
        txn.Description.ShouldBe(memo[..BankTransactionFieldLimits.DescriptionMaxLength]);
        txn.Payee!.Length.ShouldBe(BankTransactionFieldLimits.PayeeMaxLength);
    }

    /// <summary>参考号是键（银行规则按它精确匹配）：超长丢弃而不截断 —— 截出来的看似合法却是另一个参考号。</summary>
    [Fact]
    public async Task Import_DropsAnOverlongReference_InsteadOfTruncatingIt()
    {
        await SeedCoaAsync();
        var bank = await AccountIdByCodeAsync("1120");

        var result = await ImportAsync(bank, Ofx(Stmttrn("FIT-REF-1", "Payee", "Memo", checknum: new string('9', 200))));

        result.Succeeded.ShouldBeTrue(result.Message);
        (await TxnsAsync(bank)).Single().Reference.ShouldBeNull();
    }

    /// <summary>
    /// ExternalId 是去重键，超长不能截断：两个只在第 300 位不同的 FITID 截成同一个值，
    /// 第二笔真实流水会被计成重复而静默丢掉。折叠成哈希保证两者仍不同、且重传时确定性相同。
    /// </summary>
    [Fact]
    public async Task Import_FoldsAnOverlongExternalId_KeepingDistinctIdsDistinct_AndReimportIdempotent()
    {
        await SeedCoaAsync();
        var bank = await AccountIdByCodeAsync("1120");
        var prefix = new string('f', 299);
        var file = Ofx(Stmttrn(prefix + "A", "Payee A", "Memo A"), Stmttrn(prefix + "B", "Payee B", "Memo B", amount: "20.00"));

        var first = await ImportAsync(bank, file);
        first.Succeeded.ShouldBeTrue(first.Message);
        first.Data!.ImportedCount.ShouldBe(2);

        var txns = await TxnsAsync(bank);
        txns.Count.ShouldBe(2);
        txns.Select(t => t.ExternalId).Distinct().Count().ShouldBe(2);
        txns.All(t => t.ExternalId.Length <= BankTransactionFieldLimits.ExternalIdMaxLength).ShouldBeTrue();

        var second = await ImportAsync(bank, file);
        second.Succeeded.ShouldBeTrue(second.Message);
        second.Data!.ImportedCount.ShouldBe(0);
        second.Data.SkippedCount.ShouldBe(2);
    }

    /// <summary>提供者拉取与文件导入共用同一个落库口，归一化对它同样生效。</summary>
    [Fact]
    public async Task PullFromProvider_NormalizesTheSameWay()
    {
        await SeedCoaAsync();
        var bank = await AccountIdByCodeAsync("1120");
        var created = await InScopeAsync<IBankAccountService, Result<BankAccountDto>>(s => s.CreateAsync(new CreateBankAccountDto
        {
            AccountId = bank, Name = "Operating", Scheme = BankNumberScheme.UsAba, RoutingNumber = "021000021", FeedProviderKey = "stub"
        }));
        created.Succeeded.ShouldBeTrue(created.Message);

        var result = await InScopeAsync<IBankFeedService, Result<BankImportResultDto>>(s => s.PullFromProviderAsync(new PullBankFeedDto { AccountId = bank }));

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.ImportedCount.ShouldBe(1);
        var txn = (await TxnsAsync(bank)).Single();
        txn.Description!.Length.ShouldBe(BankTransactionFieldLimits.DescriptionMaxLength);
        txn.ExternalId.Length.ShouldBeLessThanOrEqualTo(BankTransactionFieldLimits.ExternalIdMaxLength);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddSingleton<IBankFeedProvider, StubOverlongProvider>();
    }

    private sealed class StubOverlongProvider : IBankFeedProvider
    {
        public string Key => "stub";

        public Task<BankFeedPullResult> PullAsync(BankFeedPullRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new BankFeedPullResult(
                [new BankFeedTransaction(new DateTime(2026, 3, 5), 10m, "USD", new string('p', 400), new string('d', 700), "Provider Payee")],
                NextCursor: null,
                LedgerBalance: null));
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

/// <summary>
/// 导入路径上还有两个越过列宽 / 币种守卫的值：上传文件名（人给的 → 400，不截断）与
/// 提供者逐行币种（文件路径在对账单级比过账户币种，提供者路径此前一行都没比）。
/// 两条都必须在批次头落库<b>之前</b>拒绝：批次头先提交、第一行再炸，留下的正是
/// <c>BankTransactionFieldLimits</c> 那次修的孤儿头形态。
/// </summary>
public class BankFeedInputGuardTests : FinanceIntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddSingleton<IBankFeedProvider, StubForeignCurrencyProvider>();
    }

    private async Task<int> BatchCountAsync(Guid account)
    {
        var repo = ServiceProvider.GetRequiredService<IRepository<BankImportBatch, Guid>>();
        return await repo.CountAsync(b => b.AccountId == account);
    }

    [Fact]
    public async Task Import_OverlongFileName_Rejects400_BeforeWritingTheBatchHeader()
    {
        await SeedCoaAsync();
        var bank = await AccountIdByCodeAsync("1120");
        var csv = "Date,Description,Amount\n2026-03-01,A,1.00\n";
        var mapping = new CsvMappingDto { HasHeader = true, DateColumn = 0, DescriptionColumn = 1, AmountColumn = 2, DateFormat = "yyyy-MM-dd" };
        var fileName = new string('f', BankTransactionFieldLimits.ImportFileNameMaxLength + 1) + ".csv";

        var result = await InScopeAsync<IBankFeedService, Result<BankImportResultDto>>(
            s => s.ImportStatementAsync(bank, BankTransactionSource.Csv, fileName, csv, mapping));

        result.Succeeded.ShouldBeFalse("the file name column is bounded and the name is operator-supplied");
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("file name");
        (await BatchCountAsync(bank)).ShouldBe(0);
    }

    [Fact]
    public async Task PullFromProvider_RowInAnotherCurrency_Rejects400_BeforeWritingTheBatchHeader()
    {
        await SeedCoaAsync();
        var bank = await AccountIdByCodeAsync("1120");
        var created = await InScopeAsync<IBankAccountService, Result<BankAccountDto>>(s => s.CreateAsync(new CreateBankAccountDto
        {
            AccountId = bank, Name = "Operating", Scheme = BankNumberScheme.UsAba, RoutingNumber = "021000021", FeedProviderKey = "foreign"
        }));
        created.Succeeded.ShouldBeTrue(created.Message);

        var result = await InScopeAsync<IBankFeedService, Result<BankImportResultDto>>(s => s.PullFromProviderAsync(new PullBankFeedDto { AccountId = bank }));

        result.Succeeded.ShouldBeFalse("a EUR row must not be cleared against a USD ledger account");
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("EUR");
        (await BatchCountAsync(bank)).ShouldBe(0);
        var repo = ServiceProvider.GetRequiredService<IRepository<BankTransaction, Guid>>();
        (await repo.CountAsync(t => t.AccountId == bank)).ShouldBe(0);
    }

    private sealed class StubForeignCurrencyProvider : IBankFeedProvider
    {
        public string Key => "foreign";

        public Task<BankFeedPullResult> PullAsync(BankFeedPullRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new BankFeedPullResult(
                [
                    new BankFeedTransaction(new DateTime(2026, 3, 5), 10m, "USD", "OK-1", "Fine"),
                    new BankFeedTransaction(new DateTime(2026, 3, 6), 20m, "EUR", "BAD-2", "Foreign")
                ],
                NextCursor: null,
                LedgerBalance: null));
    }
}
