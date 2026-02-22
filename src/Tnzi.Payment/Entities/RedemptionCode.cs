namespace Tnzi.Payment.Entities;

/// <summary>
/// 兑换码实体
/// </summary>
public class RedemptionCode : AuditedEntity<Guid>
{
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
    /// 生成兑换码
    /// </summary>
    public static string GenerateCode(int length = 12)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return new string(Enumerable.Repeat(chars, length)
            .Select(s => s[Random.Shared.Next(s.Length)]).ToArray());
    }
}
