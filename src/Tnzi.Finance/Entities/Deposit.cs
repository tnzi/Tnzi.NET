namespace Tnzi.Finance.Entities;

/// <summary>
/// 银行存款单（把待存款项账上的多笔收款一次性带进银行）
/// </summary>
/// <remarks>
/// <para>
/// <b>这张单据存在的理由</b>：<see cref="PaymentEntry"/> 的 Inbound 侧可以落在
/// <see cref="AccountSystemRole.UndepositedFunds"/> 角色科目上（<c>FinanceOptions.PostToUndepositedFunds</c>），
/// 但在此之前<b>没有任何出口</b>能把那笔余额带进银行。收支票的行当（律所、诊所、门店）
/// 攒几天去一次银行，银行流水上是<b>一行</b> 5,000.00，账上却是 7 张收款单 ——
/// <c>BankMatchEngine</c> 按「同额同向」找候选，7 : 1 永远配不上，那行流水就永远 Pending，
/// 对账做不完。用一张总额划转顶上去，则恰好丢掉待存款项唯一要携带的信息：
/// <b>这一笔存款是由哪几张收款组成的</b>。
/// </para>
/// <para>
/// 过账规则：借 <see cref="ToAccountId"/>（目标银行科目）<b>一行</b>总额 —— 单行是为了让银行
/// 流水 1 : 1 配得上；贷 <see cref="FromAccountId"/>（待存款项 / 过渡科目）每张收款一行；
/// 贷 各「其它款项」行自己指定的科目。收款行与其它款项行合计 = 借方总额。
/// </para>
/// <para>
/// 单据范式与 <see cref="Transfer"/> 一致：编号过账时分配、Posted 不可变、作废 = 引擎冲销、
/// 乐观并发 409。期间封账与已完成对账的守卫都在冲销漏斗与引擎里，本单据不另写一份。
/// </para>
/// </remarks>
public class Deposit : MultiTenantAuditedEntity<Guid>, IConcurrencyStamp
{
    /// <summary>并发标记</summary>
    public string ConcurrencyStamp { get; set; } = string.Empty;

    /// <summary>单据编号（过账时分配）</summary>
    public string? Number { get; set; }

    /// <summary>状态（Draft/Posted/Voided）</summary>
    public FinanceDocumentStatus Status { get; set; } = FinanceDocumentStatus.Draft;

    /// <summary>
    /// 来源科目 —— 收款当初落在哪（通常是 <see cref="AccountSystemRole.UndepositedFunds"/> 角色科目）。
    /// 收款行贷记此科目，可选的收款候选也按此科目反查
    /// </summary>
    public Guid FromAccountId { get; set; }

    /// <summary>目标银行科目（借方唯一一行，金额 = 存款总额）</summary>
    public Guid ToAccountId { get; set; }

    /// <summary>存款日期</summary>
    public DateTime DepositDate { get; set; }

    /// <summary>交易币种</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>捕获汇率（过账时定格）</summary>
    public decimal ExchangeRate { get; set; }

    /// <summary>存款总额（交易币；= 各行合计）</summary>
    public decimal Amount { get; set; }

    /// <summary>存款总额（本位币，过账时定格）</summary>
    public decimal BaseAmount { get; set; }

    /// <summary>外部参考号（存款单号 / 银行回单号）</summary>
    public string? Reference { get; set; }

    /// <summary>摘要</summary>
    public string? Memo { get; set; }

    /// <summary>过账凭证</summary>
    public Guid? JournalEntryId { get; set; }

    /// <summary>作废冲销凭证</summary>
    public Guid? VoidJournalEntryId { get; set; }

    /// <summary>单据行（收款行 + 其它款项行）</summary>
    public virtual ICollection<DepositLine> Lines { get; set; } = new List<DepositLine>();
}
