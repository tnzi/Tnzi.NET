namespace Tnzi.Finance.Payroll.Entities.Configs;

/// <summary>
/// 员工配置
/// </summary>
public class EmployeeConfiguration : EntityTypeConfigurationBase<Employee, Guid>
{
    public override void Configure(EntityTypeBuilder<Employee> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Code).HasMaxLength(32).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Email).HasMaxLength(256);
        builder.Property(e => e.Phone).HasMaxLength(32);
        builder.Property(e => e.AttributesJson).HasMaxLength(4000);
        builder.Property(e => e.Notes).HasMaxLength(2000);

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
