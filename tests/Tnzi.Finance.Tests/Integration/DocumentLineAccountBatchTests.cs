using Microsoft.Extensions.Logging;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 单据过账前逐行判定入账科目：只发一条科目查询，错误消息仍报单据行号。
/// </summary>
/// <remarks>
/// 被保护的缺陷：发票 / 账单 / 贷项 / 费用单过账前对每一行各查一次科目（同一科目重复出现也重复查），
/// 一张几百行的单据在过账引擎那一次批量校验之前，先跑几百个顺序往返。
/// </remarks>
public class DocumentLineAccountBatchTests : FinanceIntegrationTestBase
{
    /// <summary>字段初始化器先于基类构造函数体运行，故 ConfigureServices 被回调时它已就绪。</summary>
    private readonly SqlCapture _sql = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddSingleton<ILoggerFactory>(_sql);
    }

    public enum DocKind
    {
        Invoice,
        Bill,
        CreditMemo,
        Expense
    }

    private static readonly DateTime DocDate = new(2026, 3, 10);

    private static bool IsSalesSide(DocKind kind) => kind is DocKind.Invoice or DocKind.CreditMemo;

    private async Task<Guid> DraftAsync(DocKind kind, IReadOnlyList<Guid> lineAccounts)
    {
        switch (kind)
        {
            case DocKind.Invoice:
            {
                var customer = await InScopeAsync<ICustomerService, Result<CustomerDto>>(s => s.CreateAsync(new CreateCustomerDto { Name = $"Customer {Guid.NewGuid():N}" }));
                var draft = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.CreateDraftAsync(new CreateInvoiceDto
                {
                    CustomerId = customer.Data!.Id,
                    DocDate = DocDate,
                    Lines = lineAccounts.Select(a => new CreateInvoiceLineDto { Description = "Service", AccountId = a, Quantity = 1, UnitPrice = 10m }).ToList()
                }));
                draft.Succeeded.ShouldBeTrue(draft.Message);
                return draft.Data!.Id;
            }
            case DocKind.CreditMemo:
            {
                var customer = await InScopeAsync<ICustomerService, Result<CustomerDto>>(s => s.CreateAsync(new CreateCustomerDto { Name = $"Customer {Guid.NewGuid():N}" }));
                var draft = await InScopeAsync<ICreditMemoService, Result<CreditMemoDto>>(s => s.CreateDraftAsync(new CreateCreditMemoDto
                {
                    CustomerId = customer.Data!.Id,
                    DocDate = DocDate,
                    Lines = lineAccounts.Select(a => new CreateCreditMemoLineDto { Description = "Refund", AccountId = a, Quantity = 1, UnitPrice = 10m }).ToList()
                }));
                draft.Succeeded.ShouldBeTrue(draft.Message);
                return draft.Data!.Id;
            }
            case DocKind.Bill:
            {
                var vendor = await InScopeAsync<IVendorService, Result<VendorDto>>(s => s.CreateAsync(new CreateVendorDto { Name = $"Vendor {Guid.NewGuid():N}" }));
                var draft = await InScopeAsync<IBillService, Result<BillDto>>(s => s.CreateDraftAsync(new CreateBillDto
                {
                    VendorId = vendor.Data!.Id,
                    DocDate = DocDate,
                    Lines = lineAccounts.Select(a => new CreateBillLineDto { Description = "Supplies", AccountId = a, Quantity = 1, UnitPrice = 10m }).ToList()
                }));
                draft.Succeeded.ShouldBeTrue(draft.Message);
                return draft.Data!.Id;
            }
            default:
            {
                var bank = await AccountIdByCodeAsync("1120");
                var draft = await InScopeAsync<IExpenseService, Result<ExpenseDto>>(s => s.CreateDraftAsync(new CreateExpenseDto
                {
                    PaidFromAccountId = bank,
                    DocDate = DocDate,
                    Lines = lineAccounts.Select(a => new CreateExpenseLineDto { Description = "Supplies", AccountId = a, Amount = 10m }).ToList()
                }));
                draft.Succeeded.ShouldBeTrue(draft.Message);
                return draft.Data!.Id;
            }
        }
    }

    private async Task<Result> PostAsync(DocKind kind, Guid id) => kind switch
    {
        DocKind.Invoice => await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.PostAsync(id)),
        DocKind.CreditMemo => await InScopeAsync<ICreditMemoService, Result<CreditMemoDto>>(s => s.PostAsync(id)),
        DocKind.Bill => await InScopeAsync<IBillService, Result<BillDto>>(s => s.PostAsync(id)),
        _ => await InScopeAsync<IExpenseService, Result<ExpenseDto>>(s => s.PostAsync(id))
    };

    /// <summary>过账一张单据，返回这次过账里读科目表的查询条数</summary>
    private async Task<int> AccountReadsWhilePostingAsync(DocKind kind, IReadOnlyList<Guid> lineAccounts)
    {
        var id = await DraftAsync(kind, lineAccounts);

        _sql.Clear();
        var posted = await PostAsync(kind, id);
        posted.Succeeded.ShouldBeTrue(posted.Message);

        return _sql.Commands.Count(c => c.Contains("FROM \"Finance_Account\"", StringComparison.Ordinal));
    }

    private async Task<(Guid First, Guid Second)> TwoLineAccountsAsync(DocKind kind)
        => IsSalesSide(kind)
            ? (await AccountIdByCodeAsync("4100"), await AccountIdByCodeAsync("4900"))
            : (await AccountIdByCodeAsync("5100"), await AccountIdByCodeAsync("5200"));

    [Theory]
    [InlineData(DocKind.Invoice)]
    [InlineData(DocKind.Bill)]
    [InlineData(DocKind.CreditMemo)]
    [InlineData(DocKind.Expense)]
    public async Task Post_ReadsTheChartTheSameNumberOfTimes_ForOneLineAndForManyLines(DocKind kind)
    {
        await SeedCoaAsync();
        var (first, second) = await TwoLineAccountsAsync(kind);

        var oneLine = await AccountReadsWhilePostingAsync(kind, [first]);
        var eightLines = await AccountReadsWhilePostingAsync(kind, [first, second, first, second, first, second, first, second]);

        oneLine.ShouldBeGreaterThan(0);
        eightLines.ShouldBe(oneLine, "line accounts are loaded in one query, not one query per line");
    }

    [Theory]
    [InlineData(DocKind.Invoice)]
    [InlineData(DocKind.Bill)]
    [InlineData(DocKind.CreditMemo)]
    [InlineData(DocKind.Expense)]
    public async Task Post_WithAnInactiveLineAccount_NamesTheDocumentLine(DocKind kind)
    {
        await SeedCoaAsync();
        var (first, second) = await TwoLineAccountsAsync(kind);
        var id = await DraftAsync(kind, [first, first, second]);

        var repo = ServiceProvider.GetRequiredService<IRepository<Account, Guid>>();
        var account = await repo.FirstOrDefaultAsync(a => a.Id == second);
        account!.IsActive = false;
        await repo.UpdateAsync(account);
        await repo.SaveChangesAsync();

        var posted = await PostAsync(kind, id);

        posted.Succeeded.ShouldBeFalse();
        posted.Code.ShouldBe(400);
        posted.Message.ShouldBe($"Line 3: Account '{account.Code}' is inactive.");
    }

    [Fact]
    public async Task ExpensePost_WithAnInactivePaidFromAccount_IsReportedBeforeTheLines()
    {
        await SeedCoaAsync();
        var (first, _) = await TwoLineAccountsAsync(DocKind.Expense);
        var id = await DraftAsync(DocKind.Expense, [first]);

        var repo = ServiceProvider.GetRequiredService<IRepository<Account, Guid>>();
        foreach (var code in new[] { "1120", "5100" })
        {
            var account = await repo.FirstOrDefaultAsync(a => a.Code == code);
            account!.IsActive = false;
            await repo.UpdateAsync(account);
        }
        await repo.SaveChangesAsync();

        var posted = await PostAsync(DocKind.Expense, id);

        posted.Succeeded.ShouldBeFalse();
        posted.Message.ShouldBe("Paid-from account: Account '1120' is inactive.");
    }
}
