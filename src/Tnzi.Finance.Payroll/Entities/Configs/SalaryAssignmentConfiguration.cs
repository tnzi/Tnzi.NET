namespace Tnzi.Finance.Payroll.Entities.Configs;

/// <summary>
/// 薪资分配配置
/// </summary>
public class SalaryAssignmentConfiguration : EntityTypeConfigurationBase<SalaryAssignment, Guid>
{
    public override void Configure(EntityTypeBuilder<SalaryAssignment> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(a => a.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SalaryStructure>()
            .WithMany()
            .HasForeignKey(a => a.StructureId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(a => a.BaseAmount).HasMoneyPrecision();
        builder.Property(a => a.Notes).HasMaxLength(2000);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(a => new { a.TenantId, a.EmployeeId, a.EffectiveFrom }).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
            builder.HasIndex(a => a.TenantId);
        }
        else
        {
            builder.HasIndex(a => new { a.EmployeeId, a.EffectiveFrom }).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        }

        builder.HasIndex(a => a.StructureId);
    }
}
