namespace Tnzi.Finance.Entities;

/// <summary>
/// 供应商（A/P 往来方）
/// </summary>
public class Vendor : MultiTenantAuditedEntity<Guid>
{
    /// <summary>
    /// 供应商编码（可空；非空时租户内唯一）
    /// </summary>
    public string? Code { get; set; }

    /// <summary>
    /// 供应商名称
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
    /// 地址（多行文本）
    /// </summary>
    public string? Address { get; set; }

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
