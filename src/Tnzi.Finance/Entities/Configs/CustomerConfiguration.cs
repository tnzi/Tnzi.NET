namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 客户配置
/// </summary>
public class CustomerConfiguration : EntityTypeConfigurationBase<Customer, Guid>
{
    public override void Configure(EntityTypeBuilder<Customer> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(c => c.Code).HasMaxLength(32);
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Email).HasMaxLength(256);
        builder.Property(c => c.Phone).HasMaxLength(32);
        builder.Property(c => c.BillingAddress).HasMaxLength(500);
        builder.Property(c => c.ShippingAddress).HasMaxLength(500);
        builder.Property(c => c.Currency).HasMaxLength(8);
        builder.Property(c => c.Notes).HasMaxLength(2000);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(c => new { c.TenantId, c.Code }).IsUnique()
                .HasFilter(IndexFilterFactory.GetColumnNotNullAndIsDeletedFalse("Code"));
            builder.HasIndex(c => c.TenantId);
        }
        else
        {
            builder.HasIndex(c => c.Code).IsUnique()
                .HasFilter(IndexFilterFactory.GetColumnNotNullAndIsDeletedFalse("Code"));
        }

        builder.HasIndex(c => c.Name);
        builder.HasIndex(c => c.IsActive);
    }
}
