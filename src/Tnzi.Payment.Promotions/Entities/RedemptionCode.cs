namespace Tnzi.Payment.Promotions.Entities;

/// <summary>
/// 兑换码实体
/// </summary>
public class RedemptionCode : AuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 兑换码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 关联促销ID
    /// </summary>
    public Guid PromotionId { get; set; }

    /// <summary>
    /// 促销实体
    /// </summary>
    public virtual Promotion? Promotion { get; set; }

    /// <summary>
    /// 兑换码类型
    /// </summary>
    public RedemptionCodeType Type { get; set; }

    /// <summary>
    /// 状态
    /// </summary>
    public RedemptionCodeStatus Status { get; set; }

    /// <summary>
    /// 总数量
    /// </summary>
    public int TotalQuantity { get; set; }

    /// <summary>
    /// 已兑换数量
    /// </summary>
    public int RedeemedQuantity { get; set; }

    /// <summary>
    /// 生效时间
    /// </summary>
    public DateTime ValidFrom { get; set; }

    /// <summary>
    /// 失效时间
    /// </summary>
    public DateTime? ValidUntil { get; set; }

    /// <summary>
    /// 每用户限制
    /// </summary>
    public int? PerUserLimit { get; set; }

    /// <summary>
    /// 备注
    /// </summary>
    public string? Remarks { get; set; }

    /// <summary>
    /// 生成兑换码。
    /// </summary>
    /// <remarks>
    /// 兑换码是一个能换钱的不记名凭证，来源必须是密码学随机数：此前用 <c>Random.Shared</c>，
    /// 拿到几个已发出的码就有可能推出同一批里其余的。32 字符字母表（去掉 0/O/1/I）× 12 位 ≈ 60 bit。
    /// </remarks>
    public static string GenerateCode(int length = 12)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return RandomNumberGenerator.GetString(chars, length);
    }
}
