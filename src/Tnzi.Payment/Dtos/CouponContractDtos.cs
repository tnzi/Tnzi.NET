namespace Tnzi.Payment.Dtos;

// 留在父模块的三个优惠券 DTO —— 它们出现在**父模块自己拥有的** ICouponService 那四个方法的
// 签名上（PreviewAsync 的入参与返回、ApplyCouponAsync 的入参与返回）。促销域搬去可选子模块
// Tnzi.Payment.Promotions 时，Dtos/PromotionDtos.cs 里的另外 12 个类跟着走了，这三个不能走：
// 搬走它们，父模块的 ICouponService 就会引用一个不加载子模块时根本不存在的类型，
// 「可选」两个字当场作废。
//
// CouponApplyContext 上的 PaymentId / SubscriptionId 都是裸 Guid?，不引用任何实体，
// 因此把它留在父模块不带来任何反向依赖。

/// <summary>
/// 优惠券核销上下文：把"这张券用在哪一单上"的完整信息一次带齐，
/// 使范围校验（适用产品/计划、首单限定）与幂等（同一业务单号只核销一次）成为可能。
/// </summary>
public class CouponApplyContext
{
    /// <summary>
    /// 优惠券代码
    /// </summary>
    [Required]
    public string CouponCode { get; set; } = string.Empty;

    /// <summary>
    /// 使用者
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 业务订单号（核销幂等键的一部分）
    /// </summary>
    public string BusinessOrderNo { get; set; } = string.Empty;

    /// <summary>
    /// 折扣计算基数（未折扣、未计税的原始金额）
    /// </summary>
    public decimal OrderAmount { get; set; }

    /// <summary>
    /// 币种
    /// </summary>
    public string Currency { get; set; } = PaymentConstants.DefaultCurrency;

    /// <summary>
    /// 产品类型
    /// </summary>
    public ProductType ProductType { get; set; } = ProductType.All;

    /// <summary>
    /// 适用范围目标ID（订阅计划ID或产品ID），用于校验 ApplyScope=Plan/Product 的促销
    /// </summary>
    public Guid? ScopeId { get; set; }

    /// <summary>
    /// 关联支付ID
    /// </summary>
    public Guid? PaymentId { get; set; }

    /// <summary>
    /// 关联订阅ID
    /// </summary>
    public Guid? SubscriptionId { get; set; }

    /// <summary>
    /// 业务订单ID
    /// </summary>
    public Guid? OrderId { get; set; }
}

/// <summary>
/// 优惠券试算结果（不产生核销记录）
/// </summary>
public class CouponPreviewDto
{
    /// <summary>
    /// 促销ID
    /// </summary>
    public Guid PromotionId { get; set; }

    /// <summary>
    /// 优惠券代码
    /// </summary>
    public string CouponCode { get; set; } = string.Empty;

    /// <summary>
    /// 折扣金额
    /// </summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// 折后金额
    /// </summary>
    public decimal FinalAmount { get; set; }
}

/// <summary>
/// 优惠券使用记录 DTO
/// </summary>
public class CouponUsageDto
{
    /// <summary>
    /// 使用记录ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 优惠券代码
    /// </summary>
    public string CouponCode { get; set; } = string.Empty;

    /// <summary>
    /// 折扣金额
    /// </summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// 使用时间
    /// </summary>
    public DateTime UsedTime { get; set; }

    /// <summary>
    /// 业务订单ID
    /// </summary>
    public Guid? OrderId { get; set; }

    /// <summary>
    /// 业务订单号
    /// </summary>
    public string? BusinessOrderNo { get; set; }

    /// <summary>
    /// 关联支付ID
    /// </summary>
    public Guid? PaymentId { get; set; }
}
