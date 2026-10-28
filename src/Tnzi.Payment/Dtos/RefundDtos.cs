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
    [MaxLength(64, ErrorMessage = "TradeNo cannot exceed 64 characters.")]
    public string TradeNo { get; set; } = string.Empty;

    /// <summary>
    /// 退款金额
    /// </summary>
    [Range(0.01, double.MaxValue, ErrorMessage = "Refund amount must be greater than 0.")]
    public decimal RefundAmount { get; set; }

    /// <summary>
    /// 退款原因（列宽 500，见 <c>RefundConfiguration</c>；超长的值此前写库时才失败，调用方拿到 500）
    /// </summary>
    [Required]
    [MaxLength(500, ErrorMessage = "Reason cannot exceed 500 characters.")]
    public string Reason { get; set; } = string.Empty;
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
    /// 退款从哪里来：本模块发起，或在支付渠道后台手工退的（经渠道回调记下）。
    /// </summary>
    public RefundOrigin Origin { get; set; }

    /// <summary>
    /// 所退支付的计费账户种类（<c>User</c> / <c>Installation</c> / <c>Tenant</c>）；升级前建的、还没回填的支付为空
    /// </summary>
    public string? BillingAccountType { get; set; }

    /// <summary>
    /// 计费账户 Id（与 <see cref="BillingAccountType"/> 合起来是账户键）
    /// </summary>
    public string? BillingAccountId { get; set; }

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
    /// 按计费账户种类筛选（管理端；与 <see cref="BillingAccountId"/> 合用）
    /// </summary>
    public string? BillingAccountType { get; set; }

    /// <summary>
    /// 按计费账户 Id 筛选（管理端）
    /// </summary>
    public string? BillingAccountId { get; set; }

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
    /// 备注（存进 <c>Refund.ApproveRemark</c>，列宽 500，见 <c>RefundConfiguration</c>）
    /// </summary>
    [MaxLength(500, ErrorMessage = "Remark cannot exceed 500 characters.")]
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
