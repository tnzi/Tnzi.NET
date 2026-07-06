namespace Tnzi.Payment.Entities.Configs;

public class PromotionConfiguration : EntityTypeConfigurationBase<Promotion, Guid>
{
    public override void Configure(EntityTypeBuilder<Promotion> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(p => p.PromotionCode).HasMaxLength(32).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(128).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(500);
        builder.Property(p => p.StripeCouponId).HasMaxLength(128);
        builder.Property(p => p.DiscountValue).HasMoneyPrecision();
        builder.Property(p => p.MaxDiscountAmount).HasMoneyPrecision();
        builder.Property(p => p.MinimumOrderAmount).HasMoneyPrecision();
        // ScopeIds 存储 JSON 数组，不指定类型以保持数据库兼容性

        builder.HasMany(p => p.CouponUsages)
            .WithOne(c => c.Coupon)
            .HasForeignKey(c => c.CouponId)
            .HasPrincipalKey(p => p.Id);

        builder.HasMany(p => p.RedemptionCodes)
            .WithOne(r => r.Promotion)
            .HasForeignKey(r => r.PromotionId)
            .HasPrincipalKey(p => p.Id);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(p => new { p.TenantId, p.PromotionCode }).IsUnique();
            builder.HasIndex(p => p.TenantId);
        }
        else
        {
            builder.HasIndex(p => p.PromotionCode).IsUnique();
        }

        builder.HasIndex(p => p.IsActive);
        builder.HasIndex(p => p.StartTime);
        builder.HasIndex(p => p.EndTime);
    }
}
