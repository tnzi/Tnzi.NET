namespace Tnzi.Finance.Banking.Services.Interfaces;

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

    /// <summary>分页查询支票登记簿</summary>
    Task<Result<IPagedList<BankCheckDto>>> GetPagedAsync(CheckQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>打印支票（分配号 + 建票 + 渲染合并文档，一个事务）</summary>
    Task<Result<CheckFileDto>> PrintAsync(PrintChecksDto input, CancellationToken cancellationToken = default);

    /// <summary>
    /// 预览支票（零副作用：不分配支票号、不写登记簿、不动账）
    /// </summary>
    /// <remarks>
    /// 校验口径与 <see cref="PrintAsync"/> 一致（Posted Outbound Check + 同一银行账户 + 未开票），
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
