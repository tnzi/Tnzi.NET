namespace Tnzi.Finance.Entities;

/// <summary>
/// 银行存款单行 —— 要么是一张收款单（<see cref="PaymentEntryId"/> 非空），
/// 要么是一笔「其它款项」（<see cref="AccountId"/> 非空）
/// </summary>
/// <remarks>
/// <para>
/// 两种行由 <see cref="PaymentEntryId"/> 是否为空区分，不另设枚举：判别式若与数据分开存，
/// 迟早出现「类型说是收款行、<c>PaymentEntryId</c> 却是空」这种无法解释的行。
/// </para>
/// <para>
/// <b><see cref="ClaimedPaymentEntryId"/> 为什么与 <see cref="PaymentEntryId"/> 并存</b>：
/// 前者是<b>可释放的独占声明</b>（唯一索引落在它上面），后者是<b>永久的事实记录</b>。
/// 「一张收款至多被一张存活的存款单收走」必须由数据库保证 —— 先查再写在并发下必然漏，
/// 两个人同时把同一张支票放进各自的存款单，读到的都是「还没人收」。
/// 作废存款单时把 <see cref="ClaimedPaymentEntryId"/> 置空即释放收款，<see cref="PaymentEntryId"/>
/// 原样留着，于是「这张作废的存款单当初装的是哪几笔」仍答得出来。
/// </para>
/// <para>
/// 兄弟模块 <c>EftBatchLine</c> 走的是另一条路 —— 作废时把行硬删掉。它自己的注释里写着代价：
/// 「删完就再也答不出这个批次里装的是哪几笔」。存款单直接产生总账凭证，作废后仍是审计对象，
/// 所以这里选择留行、只放开声明。
/// </para>
/// <para>
/// 草稿阶段就持有声明（不是过账才持有）：否则两个人各自攒一份含同一张支票的草稿，
/// 第二个人要到点「过账」那一刻才发现，而那时他已经跑完一趟银行了。
/// </para>
/// </remarks>
public class DepositLine : EntityBase<Guid>, IMultiTenant
{
    /// <summary>租户ID</summary>
    public Guid? TenantId { get; set; }

    /// <summary>所属存款单</summary>
    public Guid DepositId { get; set; }

    /// <summary>行号</summary>
    public int LineNumber { get; set; }

    /// <summary>收款单（收款行非空；其它款项行为 null）</summary>
    public Guid? PaymentEntryId { get; set; }

    /// <summary>
    /// 对收款单的独占声明（唯一索引；作废时置空以释放，其它款项行恒为 null）
    /// </summary>
    public Guid? ClaimedPaymentEntryId { get; set; }

    /// <summary>
    /// 其它款项行的贷记科目（其它款项行必填；收款行为 null，贷方走存款单的来源科目）
    /// </summary>
    public Guid? AccountId { get; set; }

    /// <summary>金额（交易币；收款行恒等于收款单金额）</summary>
    public decimal Amount { get; set; }

    /// <summary>摘要</summary>
    public string? Description { get; set; }

    /// <summary>外部参考号（其它款项行的支票号 / 回单号）</summary>
    public string? Reference { get; set; }
}
