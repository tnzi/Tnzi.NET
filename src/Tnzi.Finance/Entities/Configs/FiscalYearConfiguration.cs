namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 会计年度配置
/// </summary>
public class FiscalYearConfiguration : EntityTypeConfigurationBase<FiscalYear, Guid>
{
    public override void Configure(EntityTypeBuilder<FiscalYear> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(f => f.Name).HasMaxLength(64).IsRequired();

        if (multiTenancyEnabled)
        {
            builder.HasIndex(f => new { f.TenantId, f.Name }).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
            builder.HasIndex(f => f.TenantId);
        }
        else
        {
            builder.HasIndex(f => f.Name).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        }

        builder.HasIndex(f => new { f.StartDate, f.EndDate });
    }
}
