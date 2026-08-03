namespace Tnzi.Payment.Entities.Configs;

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
        builder.HasIndex(c => c.PaymentId);
    }
}
