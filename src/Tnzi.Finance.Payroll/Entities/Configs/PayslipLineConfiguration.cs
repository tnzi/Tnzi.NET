namespace Tnzi.Finance.Payroll.Entities.Configs;

/// <summary>
/// 工资单行配置（随工资单整体重建，级联硬删）
/// </summary>
public class PayslipLineConfiguration : EntityTypeConfigurationBase<PayslipLine, Guid>
{
    public override void Configure(EntityTypeBuilder<PayslipLine> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.HasOne(l => l.Payslip)
            .WithMany(p => p.Lines)
            .HasForeignKey(l => l.PayslipId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(l => l.ComponentCode).HasMaxLength(64).IsRequired();
        builder.Property(l => l.ComponentName).HasMaxLength(200).IsRequired();
        builder.Property(l => l.FormulaSnapshot).HasMaxLength(4000);
        builder.Property(l => l.Amount).HasMoneyPrecision();
        builder.Property(l => l.YtdAmount).HasMoneyPrecision();

        builder.HasIndex(l => new { l.PayslipId, l.Sequence });

        if (multiTenancyEnabled)
            builder.HasIndex(l => l.TenantId);
    }
}
