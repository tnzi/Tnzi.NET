namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 税码配置
/// </summary>
public class TaxCodeConfiguration : EntityTypeConfigurationBase<TaxCode, Guid>
{
    public override void Configure(EntityTypeBuilder<TaxCode> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(c => c.Name).HasMaxLength(128).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(500);
        // 默认可抵扣：存量税码（如加拿大 GST/HST）迁移后须保持进项抵扣，不能默认 false 而误路由到成本科目。
        builder.Property(c => c.IsRecoverable).HasDefaultValue(true);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(c => new { c.TenantId, c.Name }).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
            builder.HasIndex(c => c.TenantId);
        }
        else
        {
            builder.HasIndex(c => c.Name).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        }
    }
}
