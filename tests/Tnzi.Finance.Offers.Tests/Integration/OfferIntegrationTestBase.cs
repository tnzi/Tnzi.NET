using Mapster;
using MapsterMapper;
using Microsoft.Extensions.Logging;
using Moq;
using Tnzi.Domain.Entities;
using Tnzi.EventBus;
using Tnzi.Finance.Offers.Services.Internal;
using Tnzi.Finance.Services.Internal;
using Tnzi.Mapster;

namespace Tnzi.Finance.Offers.Tests.Integration;

/// <summary>
/// 要约模块集成测试基类：真实 SQLite + 仓储 + UnitOfWork + 本模块两个服务，
/// 外加转换目标（发票 / 账单）与断言"从不进总账"所需的那一段 Finance 核心栈。
/// </summary>
/// <remarks>
/// ⚠️ 这是 <c>FinanceModule</c> + <c>FinanceOffersModule</c> 注册图的手工镜像
/// （沿 <c>PayrollIntegrationTestBase</c> 的先例）：核心服务新增构造依赖时必须在此同步补注册，
/// 否则本套件运行期 DI 解析崩。刻意只镜像本模块真的会走到的那一段，而不是把核心夹具整份抄来——
/// 抄来的多余注册会让"本模块只需要 Finance 核心"这句话失去证明力。
/// </remarks>
public abstract class OfferIntegrationTestBase : IntegratedTestBase<OfferTestDbContext>
{
    protected OfferIntegrationTestBase()
    {
        var config = new TypeAdapterConfig();
        MapperExtensions.SetMapper(new Mapper(config));
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddOptions();
        services.Configure<FinanceOptions>(o =>
        {
            o.BaseCurrency = "USD";
            o.JournalNumberPrefix = "JE-";
            o.JournalNumberPadding = 6;
        });
        // 号段前缀随单据住在本模块的 Options 里（同一 Finance 配置节）。
        services.Configure<FinanceOfferOptions>(o =>
        {
            o.EstimateNumberPrefix = "EST-";
            o.PurchaseOrderNumberPrefix = "PO-";
        });
        services.AddSingleton(TimeProvider.System);

        // 仓储（IRepository + IReadOnlyRepository）
        AddRepo<Account>(services);
        AddRepo<AccountPeriodBalance>(services);
        AddRepo<JournalEntry>(services);
        AddRepo<JournalLine>(services);
        AddRepo<FiscalYear>(services);
        AddRepo<LedgerLock>(services);
        AddRepo<ExchangeRate>(services);
        // 冲销守卫的判定输入
        AddRepo<Reconciliation>(services);
        AddRepo<ReconciliationLine>(services);
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
        // 供应商删除守卫查费用单
        AddRepo<Expense>(services);
        AddRepo<ExpenseLine>(services);
        AddRepo<CreditMemo>(services);
        AddRepo<CreditMemoLine>(services);
        AddRepo<PaymentEntry>(services);
        AddRepo<PaymentApplication>(services);
        AddRepo<Estimate>(services);
        AddRepo<EstimateLine>(services);
        AddRepo<PurchaseOrder>(services);
        AddRepo<PurchaseOrderLine>(services);

        // UnitOfWork（让 ExecuteInUnitOfWorkAsync 走真实延迟保存路径）
        var entityManagerMock = new Mock<IEntityManager>();
        entityManagerMock.Setup(m => m.GetAllDbContextTypes()).Returns(new[] { typeof(OfferTestDbContext) });
        entityManagerMock.Setup(m => m.Initialize());
        services.AddSingleton(_ => entityManagerMock.Object);
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();

        // EventBus（无处理器，验证发布路径不炸）
        services.AddSingleton<IEventBus>(sp =>
            new LocalEventBus(sp, sp.GetRequiredService<ILogger<LocalEventBus>>()));

        // Finance 核心：编号、科目、过账栈、报表
        services.AddScoped<IDocumentNumberService, DocumentNumberService>();
        services.AddScoped<IChartOfAccountsService, ChartOfAccountsService>();
        services.AddScoped<IExchangeRateService, ExchangeRateService>();
        services.AddScoped<ILedgerLockService, LedgerLockService>();
        services.AddScoped<IFiscalYearService, FiscalYearService>();
        services.AddScoped<LedgerPostingEngine>();
        services.AddScoped<ReversalGuard>();
        services.AddScoped<BalanceSummaryMaintainer>();
        services.AddScoped<BalanceSummaryReader>();
        services.AddScoped<GeneralLedgerReader>();
        services.AddScoped<PostingGuardRunner>();
        services.AddScoped<IJournalEntryService, JournalEntryService>();
        services.AddScoped<ILedgerPostingService, LedgerPostingService>();
        services.AddScoped<IFinancialReportService, FinancialReportService>();

        // Finance 核心：主数据与单据
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IVendorService, VendorService>();
        services.AddScoped<IItemService, ItemService>();
        services.AddScoped<ITaxService, TaxService>();
        services.AddScoped<ITaxCalculator, DefaultTaxCalculator>();
        services.AddScoped<FinanceDocumentHelper>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IBillService, BillService>();

        // 镜像 FinanceOffersModule
        services.AddScoped<OfferComposer>();
        services.AddScoped<IEstimateService, EstimateService>();
        services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
        services.AddScoped<IMasterDataUsageProvider, OfferMasterDataUsageProvider>();

        ConfigureExtraServices(services);
    }

    /// <summary>派生测试可覆盖以追加/覆盖服务。</summary>
    protected virtual void ConfigureExtraServices(IServiceCollection services)
    {
    }

    private static void AddRepo<TEntity>(IServiceCollection services) where TEntity : class, IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new EFCoreRepository<OfferTestDbContext, TEntity, Guid>(
                sp.GetRequiredService<OfferTestDbContext>(), serviceProvider: sp));
        services.AddScoped<IReadOnlyRepository<TEntity, Guid>>(sp =>
            sp.GetRequiredService<IRepository<TEntity, Guid>>());
    }

    /// <summary>
    /// 在独立 scope 中执行一次服务操作（每次操作=一个新的服务实例，贴近真实请求生命周期）
    /// </summary>
    protected async Task<TResult> InScopeAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        using var scope = ServiceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<TService>();
        return await action(svc);
    }

    /// <summary>播种默认科目表（断言成功）</summary>
    protected async Task SeedCoaAsync()
    {
        var result = await InScopeAsync<IChartOfAccountsService, Result<int>>(s => s.SeedDefaultAsync());
        result.Succeeded.ShouldBeTrue(result.Message);
    }

    /// <summary>按编码查询科目 Id</summary>
    protected async Task<Guid> AccountIdByCodeAsync(string code)
    {
        var repo = ServiceProvider.GetRequiredService<IRepository<Account, Guid>>();
        var account = await repo.FirstOrDefaultAsync(a => a.Code == code);
        account.ShouldNotBeNull($"account {code}");
        return account.Id;
    }
}
