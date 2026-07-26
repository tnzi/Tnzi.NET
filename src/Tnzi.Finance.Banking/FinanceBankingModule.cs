namespace Tnzi.Finance.Banking;

/// <summary>
/// Finance 银行域子模块：本方与往来方银行账户档案、银行流水导入与匹配、支票登记与打印生命周期、
/// EFT 批量付款文件输出、收据采集。
/// </summary>
/// <remarks>
/// <b>为什么独立</b>：这一块带着 OFX/CSV 解析、MICR 磁码拼装、NACHA / CPA-005 定宽文件构建、
/// 账号密文存储等一整套与记账无关的机制。**一个只做会计的消费方不该被迫拉入它们**，
/// 也不该凭空多出七张表、二十多个端点和一组权限码。
/// <br/><br/>
/// <b>依赖方向</b>：本模块单向依赖 Finance 核心（建单委托 <c>IExpenseService</c> /
/// <c>IPaymentEntryService</c> / <c>ITransferService</c>，过账走 <c>ILedgerPostingService</c>）。
/// <b>核心绝不反向引用本模块</b> —— 核心原先需要银行域事实的三处（冲销守卫 / 对账勾选守卫与工作区
/// 标志 / 总账关键字搜支票号）已改为经 <see cref="IJournalLineHoldProvider"/> 与
/// <see cref="IGeneralLedgerSearchContributor"/> 提问，由本模块的 <see cref="BankStatementHoldProvider"/>
/// 与 <see cref="CheckNumberSearchContributor"/> 回答。未加载本模块时两个契约无实现，核心退回
/// "无人持有 / 只搜自带项" —— 只会少拒绝、少搜到，不会放宽任何守卫。
/// <br/><br/>
/// <b>表前缀沿用 <c>Finance_</c></b>：拆的是程序集不是 schema，七张表的表名一字不变，因此
/// <b>不产生任何迁移</b>。银行对账（<c>Reconciliation</c>）刻意留在核心：勾选行引用
/// <c>JournalLine.Id</c>，对账是会计动作而不是银行集成。
/// <br/><br/>
/// <b>渲染与提取契约随域走</b>（<see cref="ICheckDocumentRenderer"/> / <see cref="IReceiptExtractor"/>
/// 都在本模块）：支票是银行票据、收据采集属银行域摄取，契约留核心会把三个银行域枚举一起钉在核心。
/// 因此 <c>Tnzi.Finance.Documents</c> 与 <c>Tnzi.Finance.Ai</c> 依赖本模块；未加载它们时支票渲染与
/// 收据提取返回 501 引导，其余生命周期照常可用。
/// </remarks>
[DependsOn(typeof(FinanceModule))]
public class FinanceBankingModule : TnziApplicationModule
{
    /// <inheritdoc />
    /// <remarks>与核心共享前缀：拆程序集不改表名，零迁移。</remarks>
    public override string? TableNamePrefix => "Finance";

    /// <inheritdoc />
    /// <remarks>
    /// 57：紧随 Finance(55) 与 Payroll/Ai(56) 之后，且**早于** <c>Tnzi.Finance.Documents</c>(58)
    /// —— 后者的支票渲染器要在本模块的支票服务解析渲染契约之前完成注册。
    /// </remarks>
    public override int LoadOrder => 57;

    /// <inheritdoc />
    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 账号 / EFT 文件的密文存储密钥。纯启动配置：轮换即废存量密文，故不作为运行时热设置。
        context.Services.AddTnziOptions<FinanceEncryptionOptions, FinanceEncryptionOptionsValidator>(context.Configuration);
        return base.PreConfigureServicesAsync(context);
    }

    /// <inheritdoc />
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 权限码随模块走：不加载银行域的宿主不会 seed 这 23 个码
        context.Services.AddTransient<IPermissionDefinitionProvider, FinanceBankingPermissions>();

        // ── 块 0：基建（账号密文 + 银行账户档案 + 往来方 remit-to）──
        context.Services.AddScoped<IFinanceDataProtector, FinanceDataProtector>();
        context.Services.AddScoped<IBankAccountService, BankAccountService>();
        context.Services.AddScoped<IPartyBankAccountService, PartyBankAccountService>();

        // ── 块 1：银行流水导入与匹配 ──
        context.Services.AddScoped<BankMatchEngine>();
        context.Services.AddScoped<BankDocumentDrafter>();
        context.Services.AddScoped<IBankRuleService, BankRuleService>();
        // 求值器 TryAdd：消费应用想换成自己的判定逻辑（按往来方历史、按模型），
        // 正常注册即可胜出，规则的存储与管理界面照旧可用。
        context.Services.TryAddScoped<IBankRuleEvaluator, BankRuleEvaluator>();
        context.Services.AddScoped<BankStatementIngestor>();
        context.Services.AddScoped<IBankFeedService, BankFeedService>();

        // 会计内核经这两个契约向银行域提问（把"内核 → 银行域"的反向依赖翻转过来）
        context.Services.AddScoped<IJournalLineHoldProvider, BankStatementHoldProvider>();
        context.Services.AddScoped<IGeneralLedgerSearchContributor, CheckNumberSearchContributor>();

        // ── 块 2：支票打印与登记 ──
        context.Services.AddScoped<CheckNumberAllocator>();
        context.Services.AddScoped<CheckBatchComposer>();
        context.Services.AddScoped<CheckIssuerResolver>();
        context.Services.AddScoped<ICheckService, CheckService>();
        // 付款单作废 → 自动作废其关联支票
        context.Services.AddEventHandler<FinanceDocumentVoidedEvent, PaymentVoidedCheckHandler>();

        // ── 块 3：EFT 批量付款（文件构建器可替换以适配银行方言）──
        context.Services.TryAddScoped<IEftFileComposer, DefaultEftFileComposer>();
        context.Services.AddScoped<IEftService, EftService>();

        // ── 块 4：收据采集（IReceiptExtractor 契约在本模块，默认实现由 Tnzi.Finance.Ai 提供；
        //    未注册时 ExtractAsync 返回 501 引导）──
        context.Services.AddScoped<IReceiptCaptureService, ReceiptCaptureService>();

        return base.ConfigureServicesAsync(context);
    }
}
