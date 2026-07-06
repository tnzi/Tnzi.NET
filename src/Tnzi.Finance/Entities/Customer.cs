namespace Tnzi.Finance.Entities;

/// <summary>
/// 客户（A/R 往来方）
/// </summary>
public class Customer : MultiTenantAuditedEntity<Guid>
{
    /// <summary>
    /// 客户编码（可空；非空时租户内唯一）
    /// </summary>
    public string? Code { get; set; }

    /// <summary>
    /// 客户名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 邮箱
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// 电话
    /// </summary>
    public string? Phone { get; set; }

    /// <summary>
    /// 账单地址（多行文本；P3 打印需要结构化时再演进）
    /// </summary>
    public string? BillingAddress { get; set; }

    /// <summary>
    /// 收货地址
    /// </summary>
    public string? ShippingAddress { get; set; }

    /// <summary>
    /// 默认交易币种（null 表示本位币）
    /// </summary>
    public string? Currency { get; set; }

    /// <summary>
    /// 付款账期天数（null 回退 <see cref="Options.FinanceOptions.DefaultPaymentTermsDays"/>）
    /// </summary>
    public int? PaymentTermsDays { get; set; }

    /// <summary>
    /// 默认税码
    /// </summary>
    public Guid? DefaultTaxCodeId { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// 备注
    /// </summary>
    public string? Notes { get; set; }
}
