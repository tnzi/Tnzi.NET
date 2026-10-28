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
        builder.Property(m => m.BillingAccountType).HasMaxLength(BillingAccountRef.TypeMaxLength).IsRequired();
        builder.Property(m => m.BillingAccountId).HasMaxLength(BillingAccountRef.IdMaxLength).IsRequired();

        if (multiTenancyEnabled)
        {
            builder.HasIndex(m => m.TenantId);
        }

        builder.HasIndex(m => m.UserId);
        builder.HasIndex(m => new { m.BillingAccountType, m.BillingAccountId, m.ChannelCode, m.IsActive });
        // 同一渠道下的同一 token 不允许重复保存（重复绑同一张卡应复用既有记录）
        builder.HasIndex(m => new { m.ChannelCode, m.Token }).IsUnique()
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        // 每个计费账户在每个渠道下至多一个默认支付方式。★ 显式具名：不具名时 EF 按列组合生成名字，
        // 换列之后旧名字与新名字之间是一次 drop + create，具名让这条约束在迁移里一眼可辨。
        builder.HasIndex(m => new { m.BillingAccountType, m.BillingAccountId, m.ChannelCode, m.IsDefault }, "IX_StoredPaymentMethod_DefaultPerAccountChannel")
            .IsUnique()
            .HasFilter(IndexFilterFactory.GetColumnTrueAndIsDeletedFalse(nameof(StoredPaymentMethod.IsDefault)));
    }
}
