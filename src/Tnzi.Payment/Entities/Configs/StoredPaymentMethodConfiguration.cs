namespace Tnzi.Payment.Entities.Configs;

/// <summary>
/// 已保存支付方式配置
/// </summary>
public class StoredPaymentMethodConfiguration : EntityTypeConfigurationBase<StoredPaymentMethod, Guid>
{
    public override void Configure(EntityTypeBuilder<StoredPaymentMethod> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(m => m.ChannelCode).HasMaxLength(32).IsRequired();
        builder.Property(m => m.ProviderCustomerId).HasMaxLength(128);
        builder.Property(m => m.Token).HasMaxLength(128).IsRequired();
        builder.Property(m => m.Brand).HasMaxLength(32);
        builder.Property(m => m.Last4).HasMaxLength(8);
        builder.Property(m => m.AccountLabel).HasMaxLength(128);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(m => m.TenantId);
        }

        builder.HasIndex(m => m.UserId);
        builder.HasIndex(m => new { m.UserId, m.ChannelCode, m.IsActive });
        // 同一渠道下的同一 token 不允许重复保存（重复绑同一张卡应复用既有记录）
        builder.HasIndex(m => new { m.ChannelCode, m.Token }).IsUnique()
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        // 每个用户在每个渠道下至多一个默认支付方式
        builder.HasIndex(m => new { m.UserId, m.ChannelCode, m.IsDefault }).IsUnique()
            .HasFilter(IndexFilterFactory.GetColumnTrueAndIsDeletedFalse(nameof(StoredPaymentMethod.IsDefault)));
    }
}
