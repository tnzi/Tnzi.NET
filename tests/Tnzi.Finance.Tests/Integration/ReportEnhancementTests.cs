namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 报表增强：GL 运行余额与来源透传、CSV 导出（公式注入转义）、税务申报汇总（TaxSummary）
/// </summary>
public class ReportEnhancementTests : FinanceIntegrationTestBase
{
    private async Task<Guid> AccountIdAsync(string code)
    {
        var repo = ServiceProvider.GetRequiredService<IRepository<Account, Guid>>();
        var account = await repo.FirstOrDefaultAsync(a => a.Code == code);
        account.ShouldNotBeNull($"account {code}");
        return account.Id;
    }

    private async Task<(Guid RateId, Guid CodeId)> CreateTaxSetupAsync(string agencyName, string rateName, decimal rate)
    {
        var agency = await InScopeAsync<ITaxService, Result<TaxAgencyDto>>(s => s.CreateAgencyAsync(new UpsertTaxAgencyDto { Name = agencyName }));
        agency.Succeeded.ShouldBeTrue(agency.Message);
        var taxRate = await InScopeAsync<ITaxService, Result<TaxRateDto>>(s => s.CreateRateAsync(new UpsertTaxRateDto
        {
            AgencyId = agency.Data!.Id,
            Name = rateName,
            Rate = rate
        }));
        taxRate.Succeeded.ShouldBeTrue(taxRate.Message);
        var code = await InScopeAsync<ITaxService, Result<TaxCodeDto>>(s => s.CreateCodeAsync(new UpsertTaxCodeDto
        {
            Name = $"Code {rateName}",
            Components = [new UpsertTaxCodeComponentDto { TaxRateId = taxRate.Data!.Id, Order = 1 }]
        }));
        code.Succeeded.ShouldBeTrue(code.Message);
        return (taxRate.Data!.Id, code.Data!.Id);
    }

    /// <summary>期初一笔（2 月 1000）+ 同日三笔（3 月 15 日 100/200/300）</summary>
    private async Task SeedLedgerForRunningBalanceAsync()
    {
        await SeedCoaAsync();
        (await PostLedgerAsync(SimpleSale(1000m, new DateTime(2026, 2, 1), "open-1"))).Succeeded.ShouldBeTrue();
        (await PostLedgerAsync(SimpleSale(100m, new DateTime(2026, 3, 15), "sale-a"))).Succeeded.ShouldBeTrue();
        (await PostLedgerAsync(SimpleSale(200m, new DateTime(2026, 3, 15), "sale-b"))).Succeeded.ShouldBeTrue();
        (await PostLedgerAsync(SimpleSale(300m, new DateTime(2026, 3, 15), "sale-c"))).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task GeneralLedger_RunningBalance_IsContinuousAcrossPages()
    {
        await SeedLedgerForRunningBalanceAsync();
        var ar = await InScopeAsync<IChartOfAccountsService, Account?>(s => s.FindByCodeAsync("1200"));

        var page1 = await InScopeAsync<IFinancialReportService, Result<GeneralLedgerReportDto>>(
            s => s.GetGeneralLedgerAsync(ar!.Id, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31),
                new PagedQueryDto { PageIndex = 1, PageSize = 2 }));

        page1.Succeeded.ShouldBeTrue(page1.Message);
        page1.Data!.OpeningBalance.ShouldBe(1000m);
        page1.Data.ClosingBalance.ShouldBe(1600m);
        var rows1 = page1.Data.Lines.Items.ToList();
        rows1.Count.ShouldBe(2);
        // 同日三笔按凭证ID稳定排序，运行余额从期初连续累加
        rows1[0].Debit.ShouldBe(100m);
        rows1[0].RunningBalance.ShouldBe(1100m);
        rows1[1].RunningBalance.ShouldBe(1300m);
        // 来源单据透传（register 场景回链）
        rows1[0].SourceType.ShouldBe("Test.Sale");
        rows1[0].SourceId.ShouldBe("sale-a");

        var page2 = await InScopeAsync<IFinancialReportService, Result<GeneralLedgerReportDto>>(
            s => s.GetGeneralLedgerAsync(ar!.Id, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31),
                new PagedQueryDto { PageIndex = 2, PageSize = 2 }));

        // 第 2 页起点 = 期初 + 页首之前行净额，与第 1 页末行连续
        var rows2 = page2.Data!.Lines.Items.ToList();
        rows2.Count.ShouldBe(1);
        rows2[0].Debit.ShouldBe(300m);
        rows2[0].RunningBalance.ShouldBe(1600m);
    }

    [Fact]
    public async Task GeneralLedgerExport_EmitsOpeningRowAndContinuousRunningBalance()
    {
        await SeedLedgerForRunningBalanceAsync();
        var ar = await InScopeAsync<IChartOfAccountsService, Account?>(s => s.FindByCodeAsync("1200"));

        var csv = await InScopeAsync<IFinancialReportService, Result<string>>(
            s => s.ExportGeneralLedgerCsvAsync(ar!.Id, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31)));

        csv.Succeeded.ShouldBeTrue(csv.Message);
        var lines = csv.Data!.TrimEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        lines.Count.ShouldBe(5); // 表头 + 期初行 + 3 明细行
        lines[0].ShouldBe("EntryNumber,PostingDate,Memo,SourceType,SourceId,PartyType,PartyId,Debit,Credit,RunningBalance");
        lines[1].ShouldContain("Opening balance");
        LastCell(lines[1]).ShouldBe(1000m);
        LastCell(lines[4]).ShouldBe(1600m); // 末行运行余额 = 期末余额
        csv.Data.ShouldContain("Test.Sale");
        csv.Data.ShouldContain("sale-c");
    }

    [Fact]
    public async Task TrialBalanceExport_EscapesFormulaInjection()
    {
        await SeedCoaAsync();
        var evil = await InScopeAsync<IChartOfAccountsService, Result<AccountDto>>(s => s.CreateAsync(new CreateAccountDto
        {
            Code = "4999",
            Name = "=HYPERLINK(\"http://evil\")",
            RootType = AccountRootType.Income
        }));
        evil.Succeeded.ShouldBeTrue(evil.Message);

        var posted = await PostLedgerAsync(new LedgerPostingRequest
        {
            PostingDate = new DateTime(2026, 3, 15),
            SourceType = "Test.Sale",
            SourceId = "evil-1",
            Lines =
            [
                new LedgerPostingLine { AccountRole = AccountSystemRole.AccountsReceivable, Debit = 10m },
                new LedgerPostingLine { AccountCode = "4999", Credit = 10m }
            ]
        });
        posted.Succeeded.ShouldBeTrue(posted.Message);

        var csv = await InScopeAsync<IFinancialReportService, Result<string>>(
            s => s.ExportTrialBalanceCsvAsync(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)));

        csv.Succeeded.ShouldBeTrue(csv.Message);
        csv.Data!.ShouldStartWith("Code,Name,RootType,");
        // 公式单元格前置单引号，且不以裸 "=" 进入任何单元格
        csv.Data.ShouldContain("'=HYPERLINK");
        csv.Data.ShouldNotContain(",=HYPERLINK");
        csv.Data.ShouldContain("Total,");
    }

    [Fact]
    public async Task AgingAndStatementExports_ProduceHeaders()
    {
        await SeedCoaAsync();
        (await PostLedgerAsync(SimpleSale(500m, new DateTime(2026, 3, 15), "sale-1"))).Succeeded.ShouldBeTrue();

        var bs = await InScopeAsync<IFinancialReportService, Result<string>>(
            s => s.ExportBalanceSheetCsvAsync(new DateTime(2026, 12, 31)));
        bs.Succeeded.ShouldBeTrue(bs.Message);
        bs.Data!.ShouldStartWith("Section,Code,Name,SubType,Balance");
        bs.Data.ShouldContain("Balance check");

        var pnl = await InScopeAsync<IFinancialReportService, Result<string>>(
            s => s.ExportProfitAndLossCsvAsync(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)));
        pnl.Succeeded.ShouldBeTrue(pnl.Message);
        pnl.Data!.ShouldContain("Net profit");

        var ar = await InScopeAsync<IFinancialReportService, Result<string>>(
            s => s.ExportArAgingCsvAsync(new DateTime(2026, 12, 31)));
        ar.Succeeded.ShouldBeTrue(ar.Message);
        ar.Data!.ShouldStartWith("Party,Current,Days1To30,Days31To60,Days61To90,Over90,Total");

        var ap = await InScopeAsync<IFinancialReportService, Result<string>>(
            s => s.ExportApAgingCsvAsync(new DateTime(2026, 12, 31)));
        ap.Succeeded.ShouldBeTrue(ap.Message);
    }

    [Fact]
    public async Task TaxSummary_AggregatesOutputAndInputByRate_AndReflectsVoid()
    {
        await SeedCoaAsync();
        var customer = await InScopeAsync<ICustomerService, Result<CustomerDto>>(s => s.CreateAsync(new CreateCustomerDto { Name = "Tax Customer" }));
        var vendor = await InScopeAsync<IVendorService, Result<VendorDto>>(s => s.CreateAsync(new CreateVendorDto { Name = "Tax Vendor" }));
        var (rateAId, codeAId) = await CreateTaxSetupAsync("Agency A", "GST 5", 5m);
        var (rateBId, codeBId) = await CreateTaxSetupAsync("Agency B", "PST 10", 10m);

        var income = await AccountIdAsync("4100");
        var opex = await AccountIdAsync("5200");
        var bank = await AccountIdAsync("1120");

        // 销项（税率 A）：发票 200 @5% → +10
        var invoice = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.CreateDraftAsync(new CreateInvoiceDto
        {
            CustomerId = customer.Data!.Id,
            DocDate = new DateTime(2026, 3, 10),
            Lines = [new CreateInvoiceLineDto { AccountId = income, Quantity = 2, UnitPrice = 100m, TaxCodeId = codeAId }]
        }));
        invoice.Succeeded.ShouldBeTrue(invoice.Message);
        var invoicePosted = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.PostAsync(invoice.Data!.Id));
        invoicePosted.Succeeded.ShouldBeTrue(invoicePosted.Message);

        // 凭证税行携带 TaxRateId（DTO 透出）
        var invoiceEntry = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
            s => s.GetAsync(invoicePosted.Data!.JournalEntryId!.Value));
        invoiceEntry.Data!.Lines.Single(l => l.TaxRateId != null).TaxRateId.ShouldBe(rateAId);

        // 销项冲减（税率 A）：贷项单 40 @5% → -2
        var memo = await InScopeAsync<ICreditMemoService, Result<CreditMemoDto>>(s => s.CreateDraftAsync(new CreateCreditMemoDto
        {
            CustomerId = customer.Data!.Id,
            DocDate = new DateTime(2026, 3, 20),
            Lines = [new CreateCreditMemoLineDto { AccountId = income, Quantity = 1, UnitPrice = 40m, TaxCodeId = codeAId }]
        }));
        memo.Succeeded.ShouldBeTrue(memo.Message);
        (await InScopeAsync<ICreditMemoService, Result<CreditMemoDto>>(s => s.PostAsync(memo.Data!.Id))).Succeeded.ShouldBeTrue();

        // 进项（税率 A）：费用 100 @5% → +5
        var expense = await InScopeAsync<IExpenseService, Result<ExpenseDto>>(s => s.CreateDraftAsync(new CreateExpenseDto
        {
            VendorId = vendor.Data!.Id,
            PaidFromAccountId = bank,
            DocDate = new DateTime(2026, 3, 22),
            Lines = [new CreateExpenseLineDto { AccountId = opex, Amount = 100m, TaxCodeId = codeAId }]
        }));
        expense.Succeeded.ShouldBeTrue(expense.Message);
        (await InScopeAsync<IExpenseService, Result<ExpenseDto>>(s => s.PostAsync(expense.Data!.Id))).Succeeded.ShouldBeTrue();

        // 进项（税率 B）：账单 1000 @10% → +100
        var bill = await InScopeAsync<IBillService, Result<BillDto>>(s => s.CreateDraftAsync(new CreateBillDto
        {
            VendorId = vendor.Data!.Id,
            DocDate = new DateTime(2026, 4, 1),
            Lines = [new CreateBillLineDto { AccountId = opex, Quantity = 1, UnitPrice = 1000m, TaxCodeId = codeBId }]
        }));
        bill.Succeeded.ShouldBeTrue(bill.Message);
        (await InScopeAsync<IBillService, Result<BillDto>>(s => s.PostAsync(bill.Data!.Id))).Succeeded.ShouldBeTrue();

        var summary = await InScopeAsync<IFinancialReportService, Result<TaxSummaryReportDto>>(
            s => s.GetTaxSummaryAsync(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)));

        summary.Succeeded.ShouldBeTrue(summary.Message);
        var report = summary.Data!;
        report.Rows.Count.ShouldBe(2);

        var rowA = report.Rows.Single(r => r.TaxRateId == rateAId);
        rowA.RateName.ShouldBe("GST 5");
        rowA.AgencyName.ShouldBe("Agency A");
        rowA.Rate.ShouldBe(5m);
        rowA.OutputTax.ShouldBe(8m);  // 10 - 2（贷项单冲减）
        rowA.InputTax.ShouldBe(5m);
        rowA.NetTax.ShouldBe(3m);

        var rowB = report.Rows.Single(r => r.TaxRateId == rateBId);
        rowB.OutputTax.ShouldBe(0m);
        rowB.InputTax.ShouldBe(100m);
        rowB.NetTax.ShouldBe(-100m);

        report.TotalOutputTax.ShouldBe(8m);
        report.TotalInputTax.ShouldBe(105m);
        report.TotalNetTax.ShouldBe(-97m);

        // 作废账单：冲销复制税维度，税率 B 归零后不再出行
        (await InScopeAsync<IBillService, Result<BillDto>>(s => s.VoidAsync(bill.Data!.Id))).Succeeded.ShouldBeTrue();

        var afterVoid = await InScopeAsync<IFinancialReportService, Result<TaxSummaryReportDto>>(
            s => s.GetTaxSummaryAsync(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)));
        afterVoid.Data!.Rows.ShouldHaveSingleItem().TaxRateId.ShouldBe(rateAId);
        afterVoid.Data.TotalInputTax.ShouldBe(5m);

        // CSV 导出
        var csv = await InScopeAsync<IFinancialReportService, Result<string>>(
            s => s.ExportTaxSummaryCsvAsync(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)));
        csv.Succeeded.ShouldBeTrue(csv.Message);
        csv.Data!.ShouldStartWith("Agency,RateName,Rate,OutputTax,InputTax,NetTax");
        csv.Data.ShouldContain("GST 5");
    }

    [Fact]
    public async Task TaxSummary_IncludesProgrammaticPostingWithTaxRateId()
    {
        await SeedCoaAsync();
        var (rateId, _) = await CreateTaxSetupAsync("Agency P", "HST 7", 7m);

        // 消费应用自定义单据经 ILedgerPostingService 过账，税行透传 TaxRateId
        var posted = await PostLedgerAsync(new LedgerPostingRequest
        {
            PostingDate = new DateTime(2026, 5, 1),
            SourceType = "Custom.Doc",
            SourceId = "doc-1",
            Lines =
            [
                new LedgerPostingLine { AccountRole = AccountSystemRole.AccountsReceivable, Debit = 107m },
                new LedgerPostingLine { AccountCode = "4100", Credit = 100m },
                new LedgerPostingLine { AccountRole = AccountSystemRole.TaxPayable, Credit = 7m, TaxRateId = rateId }
            ]
        });
        posted.Succeeded.ShouldBeTrue(posted.Message);

        var summary = await InScopeAsync<IFinancialReportService, Result<TaxSummaryReportDto>>(
            s => s.GetTaxSummaryAsync(new DateTime(2026, 5, 1), new DateTime(2026, 5, 31)));

        var row = summary.Data!.Rows.ShouldHaveSingleItem();
        row.TaxRateId.ShouldBe(rateId);
        row.OutputTax.ShouldBe(7m);
        row.NetTax.ShouldBe(7m);
    }

    private static decimal LastCell(string csvLine)
        => decimal.Parse(csvLine[(csvLine.LastIndexOf(',') + 1)..], CultureInfo.InvariantCulture);
}

/// <summary>
/// 导出行数上限：超限拒绝并提示缩小期间（独立类以覆盖 ReportExportMaxRows 配置）
/// </summary>
public class ReportExportRowLimitTests : FinanceIntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.Configure<FinanceOptions>(o => o.ReportExportMaxRows = 1);
    }

    [Fact]
    public async Task GeneralLedgerExport_OverRowLimit_FailsWithGuidance()
    {
        await SeedCoaAsync();
        (await PostLedgerAsync(SimpleSale(100m, new DateTime(2026, 3, 1), "s1"))).Succeeded.ShouldBeTrue();
        (await PostLedgerAsync(SimpleSale(200m, new DateTime(2026, 3, 2), "s2"))).Succeeded.ShouldBeTrue();

        var ar = await InScopeAsync<IChartOfAccountsService, Account?>(s => s.FindByCodeAsync("1200"));

        var csv = await InScopeAsync<IFinancialReportService, Result<string>>(
            s => s.ExportGeneralLedgerCsvAsync(ar!.Id, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)));

        csv.Succeeded.ShouldBeFalse();
        csv.Code.ShouldBe(400);
        csv.Message.ShouldNotBeNull();
        csv.Message.ShouldContain("Narrow the date range");
    }
}
