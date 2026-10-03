namespace Tnzi.Payment.Promotions.Entities;

/// <summary>
/// 优惠券使用记录配置
/// </summary>
public class CouponUsageConfiguration : EntityTypeConfigurationBase<CouponUsage, Guid>
{
    /// <summary>
    /// 配置实体
    /// </summary>
    public override void Configure(EntityTypeBuilder<CouponUsage> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(c => c.DiscountAmount).HasMoneyPrecision();
        builder.Property(c => c.BusinessOrderNo).HasMaxLength(128);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(c => c.TenantId);
        }

        builder.HasIndex(c => c.UserId);
        builder.HasIndex(c => c.CouponId);
        builder.HasIndex(c => new { c.CouponId, c.UserId });
        builder.HasIndex(c => c.CreationTime);
        // 核销幂等：同一促销 + 同一用户 + 同一业务单号只允许一条核销记录。
        // 唯一约束落在数据库，才能在并发下真正挡住重复核销（应用层查重只是快速失败路径）。
        builder.HasIndex(c => new { c.CouponId, c.UserId, c.BusinessOrderNo }).IsUnique()
            .HasFilter(IndexFilterFactory.GetColumnNotNull(nameof(CouponUsage.BusinessOrderNo)));
        // 同一张单上的券集合的乐观并发：并发核销读到同一个券集合就算出同一个槽位，只有一笔能写入。
        // 这是「不可叠加」在并发下的兜底（见 CouponUsage.OrderSlot）。存量行槽位为 null，按非空过滤排除在外。
        builder.HasIndex(c => new { c.UserId, c.BusinessOrderNo, c.OrderSlot }).IsUnique()
            .HasFilter(IndexFilterFactory.GetColumnNotNull(nameof(CouponUsage.OrderSlot)));
        builder.HasIndex(c => c.PaymentId);
    }
}
