using Mapster;
using MapsterMapper;
using Microsoft.Extensions.Logging;
using Moq;
using Tnzi.Domain.Entities;
using Tnzi.EventBus;
using Tnzi.Finance.Services.Internal;
using Tnzi.Mapster;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// Finance 集成测试基类：真实 SQLite + 仓储 + UnitOfWork + 全部财务服务，
/// 用于验证过账管线、连续编号、期间锁定与报表聚合的端到端行为。
/// </summary>
public abstract class FinanceIntegrationTestBase : IntegratedTestBase<FinanceTestDbContext>
{
    protected FinanceIntegrationTestBase()
    {
        var config = new TypeAdapterConfig();
        MapperExtensions.SetMapper(new Mapper(config));
    }

    /// <summary>
    /// 报表读路径是否消费余额汇总桶。默认 false（历史行为）；余额汇总等价测试逐 scope 翻转此开关。
    /// <see cref="Microsoft.Extensions.Options.IOptionsSnapshot{T}"/> 每 scope 重算，因此改此字段后
    /// 新 scope（<c>InScopeAsync</c>）中的服务即读到新值。
    /// </summary>
    protected bool UseBalanceSummaryOption { get; set; }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddOptions();
        services.Configure<FinanceOptions>(o =>
        {
            o.BaseCurrency = "USD";
            o.JournalNumberPrefix = "JE-";
            o.JournalNumberPadding = 6;
            o.UseBalanceSummary = UseBalanceSummaryOption;
        });
        // 全 0 的 32 字节测试密钥（AES-GCM 对任意 32 字节密钥成立）；确定性便于往返断言。
        services.Configure<FinanceEncryptionOptions>(o => o.EncryptionKey = Convert.ToBase64String(new byte[32]));
        services.AddSingleton(TimeProvider.System);

        // 仓储（IRepository + IReadOnlyRepository）
        AddRepo<Account>(services);
        AddRepo<JournalEntry>(services);
        AddRepo<JournalLine>(services);
        AddRepo<FiscalYear>(services);
        AddRepo<ExchangeRate>(services);
        AddRepo<DocumentSequence>(services);
        AddRepo<Customer>(services);
        AddRepo<Vendor>(services);
        AddRepo<Item>(services);
        AddRepo<TaxAgency>(services);
        AddRepo<TaxRate>(services);
        AddRepo<TaxCode>(services);
        AddRepo<TaxCodeComponent>(services);
        AddRepo<Invoice>(services);
        AddRepo<InvoiceLine>(services);
        AddRepo<Bill>(services);
        AddRepo<BillLine>(services);
        AddRepo<Expense>(services);
        AddRepo<ExpenseLine>(services);
        AddRepo<CreditMemo>(services);
        AddRepo<CreditMemoLine>(services);
        AddRepo<PaymentEntry>(services);
        AddRepo<PaymentApplication>(services);
        AddRepo<Transfer>(services);
        AddRepo<Reconciliation>(services);
        AddRepo<ReconciliationLine>(services);
        AddRepo<BankAccount>(services);
        AddRepo<PartyBankAccount>(services);
        AddRepo<BankTransaction>(services);
        AddRepo<BankImportBatch>(services);
        AddRepo<BankCheck>(services);
        AddRepo<EftBatch>(services);
        AddRepo<EftBatchLine>(services);
        AddRepo<Receipt>(services);
        AddRepo<AccountPeriodBalance>(services);

        // UnitOfWork（让 ExecuteInUnitOfWorkAsync 走真实延迟保存路径）
        var entityManagerMock = new Mock<IEntityManager>();
        entityManagerMock.Setup(m => m.GetAllDbContextTypes()).Returns(new[] { typeof(FinanceTestDbContext) });
        entityManagerMock.Setup(m => m.Initialize());
        services.AddSingleton(_ => entityManagerMock.Object);
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();

        // EventBus（无处理器，验证发布路径不炸）
        services.AddSingleton<IEventBus>(sp =>
            new LocalEventBus(sp, sp.GetRequiredService<ILogger<LocalEventBus>>()));

        // 财务服务
        services.AddScoped<IDocumentNumberService, DocumentNumberService>();
        services.AddScoped<IChartOfAccountsService, ChartOfAccountsService>();
        services.AddScoped<IJournalEntryService, JournalEntryService>();
        services.AddScoped<ILedgerPostingService, LedgerPostingService>();
        services.AddScoped<IExchangeRateService, ExchangeRateService>();
        services.AddScoped<IFiscalYearService, FiscalYearService>();
        services.AddScoped<IFinancialReportService, FinancialReportService>();
        services.AddScoped<LedgerPostingEngine>();
        // 余额汇总（批次 F）
        services.AddScoped<BalanceSummaryMaintainer>();
        services.AddScoped<BalanceSummaryReader>();
        services.AddScoped<IBalanceSummaryService, BalanceSummaryService>();

        // 主数据服务（P2a）
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IVendorService, VendorService>();
        services.AddScoped<IItemService, ItemService>();
        services.AddScoped<ITaxService, TaxService>();
        services.AddScoped<ITaxCalculator, DefaultTaxCalculator>();

        // 业务单据服务（P2b）
        services.AddScoped<FinanceDocumentHelper>();
        services.AddScoped<PostingGuardRunner>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IBillService, BillService>();
        services.AddScoped<IExpenseService, ExpenseService>();
        services.AddScoped<ICreditMemoService, CreditMemoService>();
        services.AddScoped<IPaymentEntryService, PaymentEntryService>();
        services.AddScoped<ISettlementService, SettlementService>();

        // P3a 银行域
        services.AddScoped<ITransferService, TransferService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();

        // 多币种深化：期末重估
        services.AddScoped<IRevaluationService, RevaluationService>();

        // P3「输出与摄取」— 块 0 基建 + 块 1 银行流水导入
        services.AddScoped<IFinanceDataProtector, FinanceDataProtector>();
        services.AddScoped<IBankAccountService, BankAccountService>();
        services.AddScoped<IPartyBankAccountService, PartyBankAccountService>();
        services.AddScoped<BankMatchEngine>();
        services.AddScoped<IBankFeedService, BankFeedService>();

        // P3「输出与摄取」— 块 2 支票打印
        services.AddScoped<CheckNumberAllocator>();
        services.AddScoped<ICheckDocumentRenderer, PdfSharpCheckRenderer>();
        services.AddScoped<ICheckService, CheckService>();

        // P3「输出与摄取」— 块 3 EFT 输出
        services.AddScoped<IEftFileComposer, DefaultEftFileComposer>();
        services.AddScoped<IEftService, EftService>();

        // P3「输出与摄取」— 块 4 收据采集（IReceiptExtractor 桩由测试按需注入）
        services.AddScoped<IReceiptCaptureService, ReceiptCaptureService>();
    }

    private static void AddRepo<TEntity>(IServiceCollection services) where TEntity : class, IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new EFCoreRepository<FinanceTestDbContext, TEntity, Guid>(
                sp.GetRequiredService<FinanceTestDbContext>(), serviceProvider: sp));
        services.AddScoped<IReadOnlyRepository<TEntity, Guid>>(sp =>
            sp.GetRequiredService<IRepository<TEntity, Guid>>());
    }

    /// <summary>
    /// 在独立 scope 中执行一次服务操作（每次操作=一个新的服务实例，贴近真实请求生命周期；
    /// 注意 SQLite 内存库共享同一 DbContext 实例，跨 scope 的身份映射由 ReloadAsync 绕过）
    /// </summary>
    protected async Task<TResult> InScopeAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        using var scope = ServiceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<TService>();
        return await action(svc);
    }

    /// <summary>
    /// 在独立 scope 中读取实体最新状态
    /// </summary>
    protected async Task<TEntity?> ReloadAsync<TEntity>(Guid id) where TEntity : class, IEntity<Guid>
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<TEntity, Guid>>();
        return await repo.FirstOrDefaultAsync(e => e.Id == id);
    }

    /// <summary>
    /// 播种默认科目表（断言成功）
    /// </summary>
    protected async Task SeedCoaAsync()
    {
        var result = await InScopeAsync<IChartOfAccountsService, Result<int>>(s => s.SeedDefaultAsync());
        result.Succeeded.ShouldBeTrue(result.Message);
    }

    /// <summary>
    /// 直接过账
    /// </summary>
    protected Task<Result<JournalEntryDto>> PostLedgerAsync(LedgerPostingRequest request)
        => InScopeAsync<ILedgerPostingService, Result<JournalEntryDto>>(s => s.PostAsync(request));

    /// <summary>按编码查询科目 Id</summary>
    protected async Task<Guid> AccountIdByCodeAsync(string code)
    {
        var repo = ServiceProvider.GetRequiredService<IRepository<Account, Guid>>();
        var account = await repo.FirstOrDefaultAsync(a => a.Code == code);
        account.ShouldNotBeNull($"account {code}");
        return account.Id;
    }

    /// <summary>创建科目并断言成功，返回 Id</summary>
    protected async Task<Guid> CreateAccountAsync(CreateAccountDto input)
    {
        var result = await InScopeAsync<IChartOfAccountsService, Result<AccountDto>>(s => s.CreateAsync(input));
        result.Succeeded.ShouldBeTrue(result.Message);
        return result.Data!.Id;
    }

    /// <summary>录入一条汇率（并断言成功）</summary>
    protected async Task UpsertRateAsync(string from, string to, decimal rate, DateTime date)
    {
        var result = await InScopeAsync<IExchangeRateService, Result<ExchangeRateDto>>(
            s => s.UpsertAsync(new UpsertExchangeRateDto { FromCurrency = from, ToCurrency = to, Rate = rate, RateDate = date }));
        result.Succeeded.ShouldBeTrue(result.Message);
    }

    /// <summary>构造一笔外币过账请求（Debit/Credit 为交易币金额）</summary>
    protected static LedgerPostingRequest Posting(DateTime date, string currency, decimal? rate, string sourceId, params LedgerPostingLine[] lines)
        => new()
        {
            PostingDate = date,
            Currency = currency,
            ExchangeRate = rate,
            SourceType = "Test.Fx",
            SourceId = sourceId,
            Lines = [.. lines]
        };

    /// <summary>
    /// 构造一笔简单销售过账请求：借 应收账款（角色），贷 4100 销售收入（编码）
    /// </summary>
    protected static LedgerPostingRequest SimpleSale(decimal amount, DateTime? date = null, string? sourceId = null)
        => new()
        {
            PostingDate = date ?? new DateTime(2026, 3, 15),
            Memo = "Test sale",
            SourceType = "Test.Sale",
            SourceId = sourceId ?? Guid.NewGuid().ToString("N"),
            Lines =
            [
                new LedgerPostingLine { AccountRole = AccountSystemRole.AccountsReceivable, Debit = amount },
                new LedgerPostingLine { AccountCode = "4100", Credit = amount }
            ]
        };
}
