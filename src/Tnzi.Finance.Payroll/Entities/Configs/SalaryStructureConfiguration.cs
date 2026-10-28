namespace Tnzi.Finance.Payroll.Entities.Configs;

/// <summary>
/// 薪资结构配置
/// </summary>
public class SalaryStructureConfiguration : EntityTypeConfigurationBase<SalaryStructure, Guid>
{
    public override void Configure(EntityTypeBuilder<SalaryStructure> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(s => s.Name).HasMaxLength(PayrollFieldLimits.NameMaxLength).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(PayrollFieldLimits.DescriptionMaxLength);

        builder.HasIndex(s => s.Name);
        builder.HasIndex(s => s.IsActive);

        if (multiTenancyEnabled)
            builder.HasIndex(s => s.TenantId);
    }
}
