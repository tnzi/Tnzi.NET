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

        if (multiTenancyEnabled)
        {
            builder.HasIndex(c => c.TenantId);
        }

        builder.HasIndex(c => c.UserId);
        builder.HasIndex(c => c.CouponId);
        builder.HasIndex(c => new { c.CouponId, c.UserId });
        builder.HasIndex(c => c.CreationTime);
    }
}
