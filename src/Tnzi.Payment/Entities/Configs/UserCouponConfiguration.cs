namespace Tnzi.Payment.Entities.Configs;

/// <summary>
/// 用户持券配置
/// </summary>
public class UserCouponConfiguration : EntityTypeConfigurationBase<UserCoupon, Guid>
{
    public override void Configure(EntityTypeBuilder<UserCoupon> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(u => u.RedemptionCode).HasMaxLength(64);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(u => u.TenantId);
        }

        builder.HasIndex(u => u.UserId);
        builder.HasIndex(u => new { u.UserId, u.Status });
        builder.HasIndex(u => new { u.UserId, u.PromotionId, u.Status });
        builder.HasIndex(u => u.RedemptionCodeId);
    }
}
