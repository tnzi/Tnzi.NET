namespace Tnzi.Payment.Entities.Configs;

/// <summary>
/// 兑换码配置
/// </summary>
public class RedemptionCodeConfiguration : EntityTypeConfigurationBase<RedemptionCode, Guid>
{
    /// <summary>
    /// 配置实体
    /// </summary>
    public override void Configure(EntityTypeBuilder<RedemptionCode> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(r => r.Code).HasMaxLength(32).IsRequired();
        builder.Property(r => r.Remarks).HasMaxLength(500);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(r => new { r.TenantId, r.Code }).IsUnique();
            builder.HasIndex(r => r.TenantId);
        }
        else
        {
            builder.HasIndex(r => r.Code).IsUnique();
        }

        builder.HasIndex(r => r.PromotionId);
        builder.HasIndex(r => r.Status);
    }
}
