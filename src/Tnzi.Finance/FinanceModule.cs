namespace Tnzi.Finance;

/// <summary>
/// 财务模块
/// 提供科目表、复式记账总账、过账管线、多币种、会计年度锁定与财务报表
/// 配置路径：Finance
/// </summary>
/// <remarks>
/// 通用性设计：任意业务单据通过 <see cref="ILedgerPostingService"/> 投影到总账
/// （来源多态引用 SourceType + SourceId），系统科目按 <see cref="AccountSystemRole"/>
/// 角色解析而非硬编码编码，消费应用可直接使用或在其上扩展自己的单据类型。
/// </remarks>
[DependsOn(typeof(EFCoreModule))]
[DependsOn(typeof(EventBusModule))]
public class FinanceModule : TnziApplicationModule
{
    /// <summary>
    /// 财务模块加载顺序
    /// </summary>
    public override int LoadOrder => 55;

    /// <summary>
    /// 表名前缀
    /// </summary>
    public override string? TableNamePrefix => "Finance";

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddTnziOptions<FinanceOptions, FinanceOptionsValidator>(context.Configuration);
        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // Code-declared permissions for this module's admin surfaces - the
        // Authorization module's PermissionDbSeeder picks every registered
        // provider up on startup (no-op when Authorization is not loaded).
        context.Services.AddTransient<IPermissionDefinitionProvider, FinancePermissions>();

        // 基建服务
        context.Services.AddScoped<IDocumentNumberService, DocumentNumberService>();
        context.Services.AddScoped<IExchangeRateService, ExchangeRateService>();
        context.Services.AddScoped<IFiscalYearService, FiscalYearService>();

        // 会计核心
        context.Services.AddScoped<IChartOfAccountsService, ChartOfAccountsService>();
        context.Services.AddScoped<IJournalEntryService, JournalEntryService>();
        context.Services.AddScoped<ILedgerPostingService, LedgerPostingService>();
        context.Services.AddScoped<LedgerPostingEngine>();

        // 主数据（P2a：往来方 / 目录 / 税模型）
        context.Services.AddScoped<ICustomerService, CustomerService>();
        context.Services.AddScoped<IVendorService, VendorService>();
        context.Services.AddScoped<IItemService, ItemService>();
        context.Services.AddScoped<ITaxService, TaxService>();
        // 税额计算器可插拔：消费应用先注册自己的实现即可整体替换
        context.Services.TryAddScoped<ITaxCalculator, DefaultTaxCalculator>();

        // 业务单据（P2b：五类单据 + 过账投影）
        context.Services.AddScoped<FinanceDocumentHelper>();
        // 过账前钩子链：消费应用注册 IFinancePostingGuard 实现即可在过账/作废/冲销前否决（如审批门）
        context.Services.AddScoped<PostingGuardRunner>();
        context.Services.AddScoped<IInvoiceService, InvoiceService>();
        context.Services.AddScoped<IBillService, BillService>();
        context.Services.AddScoped<IExpenseService, ExpenseService>();
        context.Services.AddScoped<ICreditMemoService, CreditMemoService>();
        context.Services.AddScoped<IPaymentEntryService, PaymentEntryService>();
        context.Services.AddScoped<ISettlementService, SettlementService>();

        // P3a 银行域
        context.Services.AddScoped<ITransferService, TransferService>();
        context.Services.AddScoped<IReconciliationService, ReconciliationService>();

        // 报表
        context.Services.AddScoped<IFinancialReportService, FinancialReportService>();

        return Task.CompletedTask;
    }
}
