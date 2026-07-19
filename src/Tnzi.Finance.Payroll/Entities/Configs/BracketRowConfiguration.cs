namespace Tnzi.Finance.Payroll.Entities.Configs;

/// <summary>
/// 税级行配置（随表头整体重建，级联删除）
/// </summary>
public class BracketRowConfiguration : EntityTypeConfigurationBase<BracketRow, Guid>
{
    public override void Configure(EntityTypeBuilder<BracketRow> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.HasOne<BracketTable>()
            .WithMany(t => t.Rows)
            .HasForeignKey(r => r.TableId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(r => r.LowerBound).HasMoneyPrecision();
        builder.Property(r => r.UpperBound).HasMoneyPrecision();
        builder.Property(r => r.Rate).HasRatePrecision();
        builder.Property(r => r.QuickDeduction).HasMoneyPrecision();

        builder.HasIndex(r => new { r.TableId, r.Sequence }).IsUnique();

        if (multiTenancyEnabled)
            builder.HasIndex(r => r.TenantId);
    }
}
