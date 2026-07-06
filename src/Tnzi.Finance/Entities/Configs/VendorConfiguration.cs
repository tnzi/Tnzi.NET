namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 供应商配置
/// </summary>
public class VendorConfiguration : EntityTypeConfigurationBase<Vendor, Guid>
{
    public override void Configure(EntityTypeBuilder<Vendor> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(v => v.Code).HasMaxLength(32);
        builder.Property(v => v.Name).HasMaxLength(200).IsRequired();
        builder.Property(v => v.Email).HasMaxLength(256);
        builder.Property(v => v.Phone).HasMaxLength(32);
        builder.Property(v => v.Address).HasMaxLength(500);
        builder.Property(v => v.Currency).HasMaxLength(8);
        builder.Property(v => v.Notes).HasMaxLength(2000);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(v => new { v.TenantId, v.Code }).IsUnique()
                .HasFilter(IndexFilterFactory.GetColumnNotNullAndIsDeletedFalse("Code"));
            builder.HasIndex(v => v.TenantId);
        }
        else
        {
            builder.HasIndex(v => v.Code).IsUnique()
                .HasFilter(IndexFilterFactory.GetColumnNotNullAndIsDeletedFalse("Code"));
        }

        builder.HasIndex(v => v.Name);
        builder.HasIndex(v => v.IsActive);
    }
}
