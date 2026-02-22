namespace Tnzi.Payment.Dtos;

/// <summary>
/// 创建退款 DTO
/// </summary>
public class CreateRefundDto
{
    /// <summary>
    /// 支付交易流水号
    /// </summary>
    [Required]
    public string TradeNo { get; set; } = string.Empty;

    /// <summary>
    /// 退款金额
    /// </summary>
    [Range(0.01, double.MaxValue, ErrorMessage = "Refund amount must be greater than 0.")]
    public decimal RefundAmount { get; set; }

    /// <summary>
    /// 退款原因
    /// </summary>
    [Required]
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// 备注
    /// </summary>
    public string? Remark { get; set; }
}

/// <summary>
/// 退款 DTO
/// </summary>
public class RefundDto
{
    /// <summary>
    /// 退款ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 退款流水号
    /// </summary>
    public string RefundNo { get; set; } = string.Empty;

    /// <summary>
    /// 关联支付流水号
    /// </summary>
    public string TradeNo { get; set; } = string.Empty;

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
    /// 退款状态
    /// </summary>
    public RefundStatus Status { get; set; }

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
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 退款查询 DTO
/// </summary>
public class RefundQueryDto : PagedQueryDto
{
    /// <summary>
    /// 支付交易流水号
    /// </summary>
    public string? TradeNo { get; set; }

    /// <summary>
    /// 退款流水号
    /// </summary>
    public string? RefundNo { get; set; }

    /// <summary>
    /// 退款状态
    /// </summary>
    public RefundStatus? Status { get; set; }

    /// <summary>
    /// 开始时间
    /// </summary>
    public DateTime? StartTime { get; set; }

    /// <summary>
    /// 结束时间
    /// </summary>
    public DateTime? EndTime { get; set; }
}

/// <summary>
/// 审批退款 DTO
/// </summary>
public class ApproveRefundDto
{
    /// <summary>
    /// 是否通过
    /// </summary>
    public bool Approved { get; set; }

    /// <summary>
    /// 备注
    /// </summary>
    public string? Remark { get; set; }
}

/// <summary>
/// 取消退款 DTO
/// </summary>
public class CancelRefundDto
{
    /// <summary>
    /// 取消原因
    /// </summary>
    public string? Reason { get; set; }
}
