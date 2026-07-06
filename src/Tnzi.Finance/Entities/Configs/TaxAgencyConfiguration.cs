namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 税务机构配置
/// </summary>
public class TaxAgencyConfiguration : EntityTypeConfigurationBase<TaxAgency, Guid>
{
    public override void Configure(EntityTypeBuilder<TaxAgency> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(a => a.Name).HasMaxLength(128).IsRequired();
        builder.Property(a => a.Description).HasMaxLength(500);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(a => new { a.TenantId, a.Name }).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
            builder.HasIndex(a => a.TenantId);
        }
        else
        {
            builder.HasIndex(a => a.Name).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        }
    }
}
