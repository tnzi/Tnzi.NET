namespace Tnzi.Finance.Offers;

/// <summary>
/// Finance 商业要约子模块：报价单（Estimate / Quote）与采购订单（Purchase Order）——
/// 两类**承诺谁也不约束、永远不进总账**的对外单据，及其转成发票 / 账单草稿的那一步。
/// </summary>
/// <remarks>
/// <b>业务范围一句话</b>：把一次商业往来从「我方报个价 / 我方下个单」走到「对方接受或拒绝」，
/// 成交那一刻交给会计单据接手。
/// <br/><br/>
/// <b>谁会刻意只加载 Finance 而不加载本模块</b>：只需要记账的消费方 —— 把发票/账单/费用
/// 从别处（电商订单、工时系统、外部 CRM）投影进总账，报价与下单发生在框架之外。对他们来说
/// 这四张表、二十个端点和八个权限码是纯粹的噪音：权限矩阵里多出两块永远不授予的功能面，
/// 菜单里多出两页永远打不开的列表。
/// <br/><br/>
/// <b>缺席退化成什么</b>：**少两类单据，不改任何一条会计行为**。这两类单据不产生凭证、
/// 不捕获汇率、不持有本位币金额、也从不核销，因此过账引擎、<c>ReversalGuard</c>、
/// <c>PostingGuardRunner</c>、全部财务报表、往来方子账、对账单、账龄与银行对账**都不因它缺席
/// 而改变一个字节**。唯一可感知的差异有两处：①<c>admin/finance/estimates</c> 与
/// <c>admin/finance/purchase-orders</c> 两组端点不存在（管理端菜单经 <c>moduleGate</c> 一并隐藏，
/// 不会渲染死链）；②主数据删除守卫**少问一个问题**（见下）。
/// <br/><br/>
/// <b>依赖方向：本模块 → 核心，恒定单向</b>。框架在引入这两类单据时就把接缝朝这个方向开好了，
/// 并写下了理由 —— 转换目标（发票草稿）被删掉之后来源单据必须重新可转换，而
/// <c>src/Tnzi.Finance/CLAUDE.md</c> 记的是：
/// <i>「修 = 转换时校验目标是否仍存在，不存在则重新可转换；<b>判定放在报价单一侧而不是给发票删除
/// 加守卫，依赖方向才不会反过来</b>（报价单知道发票，发票对报价单一无所知，与
/// <c>IJournalLineHoldProvider</c> 同一手法）」</i>。那次没有让发票去认识报价单，本次拆分才是
/// 一条项目引用加一个模块类，而不是一场重写。
/// <br/><br/>
/// <b>Bill / Invoice 留在核心是编译期要求，不是取舍</b>：
/// <see cref="EstimateService"/> 持有 <c>IReadOnlyRepository&lt;Invoice&gt;</c>、
/// <see cref="PurchaseOrderService"/> 把 <c>IBillService</c> 当 <c>Check.NotNull</c> 的必需依赖，
/// 两个转换端点还叠加核心的 <c>finance.document.create</c>。这些边都是「子 → 父」，
/// 因此合法；但它们同时意味着把发票或账单挪出核心会让本模块无法编译。
/// <br/><br/>
/// <b>表前缀沿用 <c>Finance_</c></b>：拆的是程序集不是 schema，四张表的表名一字不变
/// （<c>Finance_Estimate</c> / <c>Finance_EstimateLine</c> / <c>Finance_PurchaseOrder</c> /
/// <c>Finance_PurchaseOrderLine</c>），因此**不产生任何迁移**。前缀按实体所在程序集在模块容器里
/// 查得（<c>TableNamePrefixConfiguration</c>），少写这一行，四张表会安静地掉掉前缀。
/// <br/><br/>
/// <b>主数据删除守卫</b>：核心的客户 / 供应商 / 目录项删除守卫经
/// <see cref="IMasterDataUsageProvider"/> 向外提问「除了会计单据，还有别的东西在用它吗」，
/// 由本模块的 <see cref="OfferMasterDataUsageProvider"/> 回答。未加载本模块时该契约无实现，
/// 核心退回「只有会计单据算数」——与拆分前逐字一致，且**只会少拒绝，永远不会多放行**。
/// </remarks>
[DependsOn(typeof(FinanceModule))]
public class FinanceOffersModule : TnziApplicationModule
{
    /// <inheritdoc />
    /// <remarks>与核心共享前缀：拆程序集不改表名，零迁移。</remarks>
    public override string? TableNamePrefix => "Finance";

    /// <inheritdoc />
    /// <remarks>
    /// 61：Finance(55) / Payroll·Ai(56) / Banking(57) / Documents(58) / Recurring(59) /
    /// Tax.Ca(60) 之后。本模块不被任何兄弟子模块依赖，排在末位即可。
    /// </remarks>
    public override int LoadOrder => 61;

    /// <inheritdoc />
    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 两个号段前缀随单据走。配置节仍是 Finance，键路径一字不变
        // （Finance:EstimateNumberPrefix / Finance:PurchaseOrderNumberPrefix）——
        // 拆的是承载它们的类型，不是运维手里的那份 appsettings.json。
        context.Services.AddTnziOptions<FinanceOfferOptions>(context.Configuration);
        return base.PreConfigureServicesAsync(context);
    }

    /// <inheritdoc />
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 权限码随模块走：不做报价与采购的宿主不会 seed 这 8 个码。
        context.Services.AddTransient<IPermissionDefinitionProvider, FinanceOffersPermissions>();

        // 两侧单据的行结构与状态机逐字相同，共用 OfferComposer 让它们不可能各自漂移。
        context.Services.AddScoped<OfferComposer>();
        context.Services.AddScoped<IEstimateService, EstimateService>();
        context.Services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();

        // 核心的主数据删除守卫经此契约向外提问；本模块回答「还有报价单 / 采购订单行在用它」。
        context.Services.AddScoped<IMasterDataUsageProvider, OfferMasterDataUsageProvider>();

        return base.ConfigureServicesAsync(context);
    }
}
