namespace Tnzi.Finance.Entities;

/// <summary>
/// 报价单（Estimate / Quote）——发给客户的**不过账**单据
/// </summary>
/// <remarks>
/// 北美术语：美国与 QuickBooks 生态叫 Estimate，Xero 与英联邦生态叫 Quote，
/// 指同一件东西；本框架取 Estimate 作实体名，呈现层的措辞由 i18n 决定。
///
/// 与发票的三点根本差异，决定了它不能复用 <see cref="Invoice"/>：
/// <list type="number">
/// <item>**从不投影总账**——报价不是收入，也不是应收；没有过账凭证、没有捕获汇率、
///   没有本位币金额、没有核销。承诺不是事实。</item>
/// <item>**编号在"发出"时分配**而非过账时：报价单没有过账这一步，而对外发出的那
///   一刻它就成了对方手里的一张纸，必须有号。草稿不占号，与全模块一致。</item>
/// <item>**发出后仍可修改**：报价来回改价是正常商业过程，不是篡改会计记录。真正
///   不可变的是转换出来的发票。</item>
/// </list>
/// 转换 = 按本单据的行创建一张发票**草稿**（止步草稿，是否过账由人决定），并回记
/// <see cref="ConvertedToDocType"/>/<see cref="ConvertedToDocId"/>。
/// <see cref="IConcurrencyStamp"/> 防并发双转换生成两张发票。
/// </remarks>
public class Estimate : MultiTenantAuditedEntity<Guid>, IConcurrencyStamp
{
    /// <summary>并发标记</summary>
    public string ConcurrencyStamp { get; set; } = string.Empty;

    /// <summary>单据编号（发出时分配，可空唯一过滤索引）</summary>
    public string? Number { get; set; }

    /// <summary>状态</summary>
    public FinanceOfferStatus Status { get; set; } = FinanceOfferStatus.Draft;

    /// <summary>客户</summary>
    public Guid CustomerId { get; set; }

    /// <summary>客户导航</summary>
    public virtual Customer? Customer { get; set; }

    /// <summary>单据日期（date-only，UTC 午夜）</summary>
    public DateTime DocDate { get; set; }

    /// <summary>报价有效期至（date-only；到期与否由呈现层按当天判断，不设独立状态避免两个真相）</summary>
    public DateTime? ExpiryDate { get; set; }

    /// <summary>交易币种</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>行小计（交易币，不含税）</summary>
    public decimal SubTotal { get; set; }

    /// <summary>税额合计（交易币）</summary>
    public decimal TaxTotal { get; set; }

    /// <summary>价税合计（交易币）</summary>
    public decimal Total { get; set; }

    /// <summary>客户可见的摘要</summary>
    public string? Memo { get; set; }

    /// <summary>内部备注（不随单据发给客户）</summary>
    public string? InternalNote { get; set; }

    /// <summary>转换目标单据类型（wire 令牌，见 FinanceSourceTypes）</summary>
    public string? ConvertedToDocType { get; set; }

    /// <summary>转换目标单据 Id</summary>
    public Guid? ConvertedToDocId { get; set; }

    /// <summary>单据行</summary>
    public virtual ICollection<EstimateLine> Lines { get; set; } = new List<EstimateLine>();
}
