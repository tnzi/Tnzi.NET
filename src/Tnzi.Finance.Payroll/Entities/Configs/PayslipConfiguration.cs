namespace Tnzi.Finance.Payroll.Entities.Configs;

/// <summary>
/// 工资单配置（(批次, 员工) 唯一；批次删除时由服务在工作单元内级联清理）
/// </summary>
public class PayslipConfiguration : EntityTypeConfigurationBase<Payslip, Guid>
{
    public override void Configure(EntityTypeBuilder<Payslip> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.HasOne<PayRun>()
            .WithMany(r => r.Payslips)
            .HasForeignKey(p => p.PayRunId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(p => p.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(p => p.EmployeeCode).HasMaxLength(64).IsRequired();
        builder.Property(p => p.EmployeeName).HasMaxLength(200).IsRequired();
        builder.Property(p => p.PaymentMethod).HasMaxLength(32);
        builder.Property(p => p.CalculationError).HasMaxLength(1000);
        builder.Property(p => p.BaseAmount).HasMoneyPrecision();
        builder.Property(p => p.PeriodDays).HasMoneyPrecision();
        builder.Property(p => p.WorkedDays).HasMoneyPrecision();
        builder.Property(p => p.GrossPay).HasMoneyPrecision();
        builder.Property(p => p.TotalDeductions).HasMoneyPrecision();
        builder.Property(p => p.EmployerCost).HasMoneyPrecision();
        builder.Property(p => p.NetPay).HasMoneyPrecision();

        if (multiTenancyEnabled)
        {
            builder.HasIndex(p => new { p.TenantId, p.PayRunId, p.EmployeeId }).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
            builder.HasIndex(p => new { p.TenantId, p.EmployeeId });
        }
        else
        {
            builder.HasIndex(p => new { p.PayRunId, p.EmployeeId }).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
            builder.HasIndex(p => p.EmployeeId);
        }

        builder.HasIndex(p => p.PayRunId);
    }
}
