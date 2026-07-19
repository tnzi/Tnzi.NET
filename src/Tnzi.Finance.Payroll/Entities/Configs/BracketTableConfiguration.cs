namespace Tnzi.Finance.Payroll.Entities.Configs;

/// <summary>
/// 税级表头配置
/// </summary>
public class BracketTableConfiguration : EntityTypeConfigurationBase<BracketTable, Guid>
{
    public override void Configure(EntityTypeBuilder<BracketTable> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(t => t.Code).HasMaxLength(64).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(500);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(t => new { t.TenantId, t.Code, t.EffectiveFrom }).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
            builder.HasIndex(t => t.TenantId);
        }
        else
        {
            builder.HasIndex(t => new { t.Code, t.EffectiveFrom }).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        }

        builder.HasIndex(t => t.IsActive);
    }
}
