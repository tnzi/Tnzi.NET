namespace Tnzi.Finance.Banking.Services;

/// <summary>
/// 支票打印与登记服务
/// </summary>
/// <remarks>
/// 队列 = Posted Outbound + PaymentMethod==Check + 付款科目有银行档案 + 无关联 Issued 票；
/// 打印在一个 UoW 内逐张分配号→建票→渲染合并 PDF→提交（渲染失败经 UnitOfWorkAbortException 整体回滚，号码回收）。
/// 支票号占号留痕（Issued/Void/Spoiled 三态），无删除端点。
/// </remarks>
public interface ICheckService
{
    /// <summary>打印队列（可选按银行账户过滤）</summary>
    Task<Result<List<CheckQueueItemDto>>> GetQueueAsync(Guid? bankAccountId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 可选版式清单（出厂内置版式 + 模板库里用户自建的模板）
    /// </summary>
    /// <remarks>
    /// 供管理端画版式选择器：选中项写进 <c>BankAccount.CheckTemplateName</c>，
    /// 或作为一次打印 / 预览的单次覆盖（<see cref="PrintChecksDto.TemplateName"/>）。
    /// 未加载 <c>Tnzi.Finance.Documents</c>（内置版式随它分发）时返回 <b>501</b> 引导。
    /// </remarks>
    Task<Result<List<CheckTemplateDto>>> GetTemplatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 某套版式的<b>样张</b>：用占位数据渲染一张，供选版式时比对手上的票纸。<b>零副作用</b>
    /// （不分配支票号、不写登记簿、不动账，不需要任何真实付款单 / 收款人 / 银行账户）。
    /// </summary>
    /// <remarks>
    /// 回答的是「给我看看这套版式长什么样」——在此之前这个问题只能靠跑一次真实付款预览，
    /// 而那要求先有一张已过账、且挂着收款人的付款单。
    /// <para>
    /// ★ 走与打印<b>同一条渲染路径</b>（同一个 <see cref="ICheckDocumentRenderer"/>、同一个模型工厂），
    /// 否则样张就不是「所见即所印」，这个功能反而有害。占位数据由框架自带
    /// （见 <c>CheckSpecimenSample</c>），消费应用不必各编一套。
    /// </para>
    /// <para>
    /// ★ 张数取自版式声明的「每页几张」：每页三张的版式画三张，否则看不出它是三联的。
    /// 全票面打 <c>SPECIMEN - NOT NEGOTIABLE</c>（不是 "PREVIEW"：样张不是某笔付款的预览）。
    /// </para>
    /// <para>
    /// ★ <b>永不接触银行档案里的账号</b>：样张会被下载被打印，而白纸票纸下磁码行是真的印出来的，
    /// 水印挡人眼挡不住读票机。绑定档案只借银行名 / 路由号 / 档案名，账号一律用全 0 占位。
    /// </para>
    /// 未加载 <c>Tnzi.Finance.Documents</c> 返回 <b>501</b>（与目录端点同构）；
    /// 目录里没有这个名字返回 <b>404</b>。
    /// </remarks>
    /// <param name="templateName">版式名（取自 <see cref="GetTemplatesAsync"/>）。</param>
    /// <param name="stockType">看哪一种票纸：预印票纸只打可变数据，白纸整张打印含磁码带。</param>
    /// <param name="bankAccountId">
    /// 可选：借这个档案的银行标识与偏移，让样张贴近真实打印结果；null = 中性占位。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<Result<CheckFileDto>> GetTemplateSpecimenAsync(
        string templateName,
        CheckStockType stockType = CheckStockType.PrePrinted,
        Guid? bankAccountId = null,
        CancellationToken cancellationToken = default);

    /// <summary>分页查询支票登记簿</summary>
    Task<Result<IPagedList<BankCheckDto>>> GetPagedAsync(CheckQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>打印支票（分配号 + 建票 + 渲染合并文档，一个事务）</summary>
    Task<Result<CheckFileDto>> PrintAsync(PrintChecksDto input, CancellationToken cancellationToken = default);

    /// <summary>
    /// 预览支票（零副作用：不分配支票号、不写登记簿、不动账）
    /// </summary>
    /// <remarks>
    /// 校验口径与 <see cref="PrintAsync"/> 一致（Posted Outbound Check + 同一银行账户 + 未开票，
    /// 均由 <c>CheckBatchComposer.ResolveBatchAsync</c> 自己判定，不依赖队列过滤），
    /// 保证"所见即将打"；支票号取 <c>BankAccount.NextCheckNumber</c> 起的连号**预览值**
    /// （peek 不 consume，真正分配发生在 <see cref="PrintAsync"/>），渲染请求带
    /// <c>IsPreview=true</c> 供模板打上不可流通标记。
    /// </remarks>
    Task<Result<CheckFileDto>> PreviewAsync(PreviewChecksDto input, CancellationToken cancellationToken = default);

    /// <summary>
    /// 临时（无付款单）支票预览：直接从"将要支付"的明细渲染，<b>零副作用</b>
    /// （不过账、不建付款单、不分配支票号、不写登记簿）。
    /// </summary>
    /// <remarks>
    /// 用于"先预览、点打印才落库"的支付流：预览时账单尚未结算，没有付款单可引用。
    /// 从 <c>FundsAccountId</c> 解析银行账户档案,支票号取其 <c>NextCheckNumber</c> 起的连号预览值
    /// （peek 不 consume），带 <c>IsPreview=true</c>。渲染与 <see cref="PreviewAsync"/>/<see cref="PrintAsync"/>
    /// 共用同一模型工厂,故"所见即将打"。
    /// <para>
    /// ★ <b>框架不为它出端点，消费方自己接线时必须挂写码 <c>finance.check.create</c></b>（与 preview / render 同级）：
    /// 它虽零副作用，渲染的却是完整票面 —— 对 <c>CheckStockType.Blank</c> 的档案，磁码行由解密后的
    /// 真账号编成，水印挡得住人眼挡不住读票机，产物与 <c>{id}/render</c> 同属可流通级。
    /// 挂在只读门（<c>finance.check.view</c>）下，就把 <c>preview</c> 端点 2026-09-12 才关上的口子在消费方那层原样打开。
    /// </para>
    /// </remarks>
    Task<Result<CheckFileDto>> PreviewAdHocAsync(AdHocCheckPreviewDto input, CancellationToken cancellationToken = default);

    /// <summary>登记手工支票（显式号，撞号 409）</summary>
    Task<Result<BankCheckDto>> RegisterManualAsync(RegisterManualCheckDto input, CancellationToken cancellationToken = default);

    /// <summary>作废支票（Issued → Void，号码留痕）</summary>
    Task<Result<BankCheckDto>> VoidAsync(Guid id, VoidCheckDto input, CancellationToken cancellationToken = default);

    /// <summary>登记毁票（占号留痕，推进 NextCheckNumber）</summary>
    Task<Result<BankCheckDto>> SpoilAsync(SpoilCheckDto input, CancellationToken cancellationToken = default);

    /// <summary>重打支票（原票作废 Reprinted + 新票，形成 ReplacedByCheckId 链）</summary>
    Task<Result<CheckFileDto>> ReprintAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 重新渲染一张已开支票（同号重打，<b>零副作用</b>：不分配号、不建新票、不改状态）。
    /// </summary>
    /// <remarks>
    /// 用于"票据已开出但纸没打成"——打印机故障、卡纸、操作员关掉了打印对话框。
    /// 与 <see cref="ReprintAsync"/> 的分工:
    /// <list type="bullet">
    /// <item>本方法 = 纸<b>没出来</b>,票面内容不变,原号重出一张纸;登记簿不动。</item>
    /// <item><see cref="ReprintAsync"/> = 纸<b>出来了但作废了</b>(打坏/串行/丢失),原票转 Void
    /// 并分配新号,形成 <c>ReplacedByCheckId</c> 重打链。</item>
    /// </list>
    /// 内容取自登记簿快照（号/收款人/金额/币种/签发日）,摘要取自关联付款单,故与首次打印逐字一致。
    /// <para>
    /// <b>内控说明</b>:本方法可被重复调用,理论上可产生多张同号纸质支票。这是刻意的——框架无从
    /// 得知浏览器/打印机那一侧到底出没出纸,写一个 <c>PrintedTime</c> 无论写不写都是在撒谎。
    /// 同号重出的风险由 positive-pay 清单(同一号只上送一次)与银行只兑付一次来兜底,与
    /// 主流会计软件(QuickBooks 等)的重打行为一致。呈现端应当提示操作员。
    /// </para>
    /// </remarks>
    Task<Result<CheckFileDto>> RenderAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>生成校准标尺测试页</summary>
    Task<Result<CheckFileDto>> GetCalibrationPdfAsync(Guid bankAccountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 导出 positive-pay 已开票文件（CSV）：某银行账户在 [from, to]（按签发日）内的全部支票
    /// （支票号 / 金额 / 签发日 / 收款人 / 签发或作废标志），供上送银行的支票防伪核对服务。
    /// 未在此清单中的支票（伪造/篡改）与已作废的支票被银行拒付。
    /// </summary>
    Task<Result<string>> ExportPositivePayAsync(Guid bankAccountId, DateTime from, DateTime to, CancellationToken cancellationToken = default);

    /// <summary>作废某付款单关联的全部 Issued 票（付款作废事件联动）</summary>
    Task<Result> VoidByPaymentAsync(Guid paymentEntryId, string reason, CancellationToken cancellationToken = default);
}
