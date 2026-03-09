namespace Tnzi.Payment.Entities;

/// <summary>
/// 退款记录实体
/// </summary>
public class Refund : MultiTenantAuditedEntity<Guid>
{
    /// <summary>
    /// 退款流水号
    /// </summary>
    public string RefundNo { get; set; } = string.Empty;

    /// <summary>
    /// 关联支付ID
    /// </summary>
    public Guid PaymentId { get; set; }

    /// <summary>
    /// 关联支付实体
    /// </summary>
    public virtual Payment? Payment { get; set; }

    /// <summary>
    /// 退款金额
    /// </summary>
    public decimal RefundAmount { get; set; }

    /// <summary>
    /// 币种
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// 退款原因
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// 退款类型
    /// </summary>
    public RefundType RefundType { get; set; }

    /// <summary>
    /// 退款状态
    /// </summary>
    public RefundStatus Status { get; set; }

    /// <summary>
    /// 外部退款流水号
    /// </summary>
    public string? ExternalRefundNo { get; set; }

    /// <summary>
    /// 审批人ID
    /// </summary>
    public Guid? ApproverId { get; set; }

    /// <summary>
    /// 审批时间
    /// </summary>
    public DateTime? ApproveTime { get; set; }

    /// <summary>
    /// 审批备注
    /// </summary>
    public string? ApproveRemark { get; set; }

    /// <summary>
    /// 退款完成时间
    /// </summary>
    public DateTime? CompletedTime { get; set; }

    /// <summary>
    /// 业务订单号
    /// </summary>
    public string BusinessOrderNo { get; set; } = string.Empty;

    /// <summary>
    /// 生成退款流水号
    /// </summary>
    public static string GenerateRefundNo()
    {
        return $"REF{IdHelper.NextId()}";
    }
}
