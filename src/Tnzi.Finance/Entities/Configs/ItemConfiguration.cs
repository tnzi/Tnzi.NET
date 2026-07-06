namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 目录项配置
/// </summary>
public class ItemConfiguration : EntityTypeConfigurationBase<Item, Guid>
{
    public override void Configure(EntityTypeBuilder<Item> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(i => i.Code).HasMaxLength(32);
        builder.Property(i => i.Name).HasMaxLength(200).IsRequired();
        builder.Property(i => i.Description).HasMaxLength(1000);
        builder.Property(i => i.SalesPrice).HasMoneyPrecision();
        builder.Property(i => i.PurchasePrice).HasMoneyPrecision();

        if (multiTenancyEnabled)
        {
            builder.HasIndex(i => new { i.TenantId, i.Code }).IsUnique()
                .HasFilter(IndexFilterFactory.GetColumnNotNullAndIsDeletedFalse("Code"));
            builder.HasIndex(i => i.TenantId);
        }
        else
        {
            builder.HasIndex(i => i.Code).IsUnique()
                .HasFilter(IndexFilterFactory.GetColumnNotNullAndIsDeletedFalse("Code"));
        }

        builder.HasIndex(i => i.Name);
        builder.HasIndex(i => i.IsActive);
    }
}
