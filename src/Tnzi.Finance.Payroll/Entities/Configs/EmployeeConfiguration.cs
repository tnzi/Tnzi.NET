namespace Tnzi.Finance.Payroll.Entities.Configs;

/// <summary>
/// 员工配置
/// </summary>
public class EmployeeConfiguration : EntityTypeConfigurationBase<Employee, Guid>
{
    public override void Configure(EntityTypeBuilder<Employee> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Code).HasMaxLength(PayrollFieldLimits.EmployeeCodeMaxLength).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(PayrollFieldLimits.NameMaxLength).IsRequired();
        builder.Property(e => e.Email).HasMaxLength(PayrollFieldLimits.EmailMaxLength);
        builder.Property(e => e.Phone).HasMaxLength(PayrollFieldLimits.PhoneMaxLength);
        builder.Property(e => e.AttributesJson).HasMaxLength(PayrollFieldLimits.AttributesJsonMaxLength);
        builder.Property(e => e.Notes).HasMaxLength(PayrollFieldLimits.NotesMaxLength);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.Code }).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
            builder.HasIndex(e => e.TenantId);
        }
        else
        {
            builder.HasIndex(e => e.Code).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        }

        builder.HasIndex(e => e.Name);
        builder.HasIndex(e => e.IsActive);
    }
}
