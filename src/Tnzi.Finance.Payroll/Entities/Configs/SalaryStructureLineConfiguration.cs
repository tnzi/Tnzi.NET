namespace Tnzi.Finance.Payroll.Entities.Configs;

/// <summary>
/// 薪资结构行配置（随结构整体重建，级联删除；组件被引用时禁删）
/// </summary>
public class SalaryStructureLineConfiguration : EntityTypeConfigurationBase<SalaryStructureLine, Guid>
{
    public override void Configure(EntityTypeBuilder<SalaryStructureLine> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.HasOne<SalaryStructure>()
            .WithMany(s => s.Lines)
            .HasForeignKey(l => l.StructureId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<SalaryComponent>()
            .WithMany()
            .HasForeignKey(l => l.ComponentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(l => l.FormulaOverride).HasMaxLength(4000);
        builder.Property(l => l.ConditionOverride).HasMaxLength(4000);
        builder.Property(l => l.AmountOverride).HasMoneyPrecision();

        builder.HasIndex(l => new { l.StructureId, l.ComponentId }).IsUnique();
        builder.HasIndex(l => l.ComponentId);

        if (multiTenancyEnabled)
            builder.HasIndex(l => l.TenantId);
    }
}
