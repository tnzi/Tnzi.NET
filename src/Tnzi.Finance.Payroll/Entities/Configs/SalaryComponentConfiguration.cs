namespace Tnzi.Finance.Payroll.Entities.Configs;

/// <summary>
/// 薪资组件配置
/// </summary>
public class SalaryComponentConfiguration : EntityTypeConfigurationBase<SalaryComponent, Guid>
{
    public override void Configure(EntityTypeBuilder<SalaryComponent> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(c => c.Code).HasMaxLength(64).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        // 列宽与 PayrollOptionsValidator 的 FormulaMaxLength 上限（4000）对齐
        builder.Property(c => c.Formula).HasMaxLength(4000);
        builder.Property(c => c.Condition).HasMaxLength(4000);
        builder.Property(c => c.DefaultAmount).HasMoneyPrecision();
        builder.Property(c => c.Description).HasMaxLength(500);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(c => new { c.TenantId, c.Code }).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
            builder.HasIndex(c => c.TenantId);
        }
        else
        {
            builder.HasIndex(c => c.Code).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        }

        builder.HasIndex(c => c.IsActive);
    }
}
