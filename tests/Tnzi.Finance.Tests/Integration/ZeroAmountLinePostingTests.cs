namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 零额行（免费赠送、0 元运费、选了目录项还没填价）能存进草稿，也必须能过账。
/// </summary>
/// <remarks>
/// 被保护的缺陷：草稿只拒绝负单价，零单价的行照常保存；过账时每个单据行原样变成一条分录行，
/// 引擎以「借贷恰好一方大于零」拒绝它，报的「Line 2」还是凭证行号（第 1 行是应收/应付），
/// 与单据上的行号对不上。单据在有人找到并删掉那一行之前永远过不了账。零额行在单据上是合法的展示行，
/// 它不进总账。
/// </remarks>
public class ZeroAmountLinePostingTests : FinanceIntegrationTestBase
{
    private async Task<Guid> AccountIdAsync(string code)
    {
        var repo = ServiceProvider.GetRequiredService<IRepository<Account, Guid>>();
        var account = await repo.FirstOrDefaultAsync(a => a.Code == code);
        account.ShouldNotBeNull($"account {code}");
        return account.Id;
    }

    private async Task<JournalEntryDto> EntryAsync(Guid id)
    {
        var entry = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(s => s.GetAsync(id));
        entry.Succeeded.ShouldBeTrue(entry.Message);
        return entry.Data!;
    }

    [Fact]
    public async Task Invoice_WithAZeroAmountLine_Posts_AndTheLineStaysOutOfTheLedger()
    {
        await SeedCoaAsync();
        var customer = await InScopeAsync<ICustomerService, Result<CustomerDto>>(s => s.CreateAsync(new CreateCustomerDto { Name = "Zero Co" }));
        var income = await AccountIdAsync("4100");

        var draft = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.CreateDraftAsync(new CreateInvoiceDto
        {
            CustomerId = customer.Data!.Id,
            DocDate = new DateTime(2026, 3, 10),
            Lines =
            [
                new CreateInvoiceLineDto { Description = "Consulting", AccountId = income, Quantity = 2, UnitPrice = 100m },
                new CreateInvoiceLineDto { Description = "Free shipping", AccountId = income, Quantity = 1, UnitPrice = 0m },
            ]
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);

        var posted = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.PostAsync(draft.Data!.Id));

        posted.Succeeded.ShouldBeTrue(posted.Message);
        posted.Data!.Total.ShouldBe(200m);
        posted.Data.Lines.Count.ShouldBe(2, "the zero line stays on the document");
        var entry = await EntryAsync(posted.Data.JournalEntryId!.Value);
        entry.Lines.ShouldAllBe(l => l.Debit != 0 || l.Credit != 0);
        entry.Lines.Count.ShouldBe(2);
        entry.TotalDebit.ShouldBe(200m);
        entry.TotalCredit.ShouldBe(200m);
    }

    [Fact]
    public async Task Bill_WithAZeroAmountLine_Posts_AndTheLineStaysOutOfTheLedger()
    {
        await SeedCoaAsync();
        var vendor = await InScopeAsync<IVendorService, Result<VendorDto>>(s => s.CreateAsync(new CreateVendorDto { Name = "Zero Vendor" }));
        var expense = await AccountIdAsync("5200");

        var draft = await InScopeAsync<IBillService, Result<BillDto>>(s => s.CreateDraftAsync(new CreateBillDto
        {
            VendorId = vendor.Data!.Id,
            DocDate = new DateTime(2026, 3, 10),
            Lines =
            [
                new CreateBillLineDto { Description = "Widget", AccountId = expense, Quantity = 1, UnitPrice = 80m },
                new CreateBillLineDto { Description = "Sample, no charge", AccountId = expense, Quantity = 3, UnitPrice = 0m },
            ]
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);

        var posted = await InScopeAsync<IBillService, Result<BillDto>>(s => s.PostAsync(draft.Data!.Id));

        posted.Succeeded.ShouldBeTrue(posted.Message);
        posted.Data!.Total.ShouldBe(80m);
        var entry = await EntryAsync(posted.Data.JournalEntryId!.Value);
        entry.Lines.ShouldAllBe(l => l.Debit != 0 || l.Credit != 0);
        entry.Lines.Count.ShouldBe(2);
        entry.TotalDebit.ShouldBe(80m);
    }

    [Fact]
    public async Task CreditMemo_WithAZeroAmountLine_Posts_AndTheLineStaysOutOfTheLedger()
    {
        await SeedCoaAsync();
        var customer = await InScopeAsync<ICustomerService, Result<CustomerDto>>(s => s.CreateAsync(new CreateCustomerDto { Name = "Zero Credit Co" }));
        var income = await AccountIdAsync("4100");

        var draft = await InScopeAsync<ICreditMemoService, Result<CreditMemoDto>>(s => s.CreateDraftAsync(new CreateCreditMemoDto
        {
            CustomerId = customer.Data!.Id,
            DocDate = new DateTime(2026, 3, 10),
            Lines =
            [
                new CreateCreditMemoLineDto { Description = "Refund", AccountId = income, Quantity = 1, UnitPrice = 30m },
                new CreateCreditMemoLineDto { Description = "Goodwill note", AccountId = income, Quantity = 1, UnitPrice = 0m },
            ]
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);

        var posted = await InScopeAsync<ICreditMemoService, Result<CreditMemoDto>>(s => s.PostAsync(draft.Data!.Id));

        posted.Succeeded.ShouldBeTrue(posted.Message);
        posted.Data!.Total.ShouldBe(30m);
        var entry = await EntryAsync(posted.Data.JournalEntryId!.Value);
        entry.Lines.ShouldAllBe(l => l.Debit != 0 || l.Credit != 0);
        entry.Lines.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Invoice_WhoseLinesAreAllZero_IsStillRejected()
    {
        await SeedCoaAsync();
        var customer = await InScopeAsync<ICustomerService, Result<CustomerDto>>(s => s.CreateAsync(new CreateCustomerDto { Name = "All Zero Co" }));
        var income = await AccountIdAsync("4100");

        var draft = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.CreateDraftAsync(new CreateInvoiceDto
        {
            CustomerId = customer.Data!.Id,
            DocDate = new DateTime(2026, 3, 10),
            Lines = [new CreateInvoiceLineDto { Description = "Nothing", AccountId = income, Quantity = 1, UnitPrice = 0m }]
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);

        var posted = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.PostAsync(draft.Data!.Id));

        posted.Succeeded.ShouldBeFalse();
        posted.Code.ShouldBe(400);
        posted.Message!.ShouldContain("greater than zero");
    }
}
