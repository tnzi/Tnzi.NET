namespace Tnzi.Finance.Payroll.Entities.Configs;

/// <summary>
/// 发薪批次配置
/// </summary>
public class PayRunConfiguration : EntityTypeConfigurationBase<PayRun, Guid>
{
    public override void Configure(EntityTypeBuilder<PayRun> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(r => r.Number).HasMaxLength(64);
        builder.Property(r => r.Memo).HasMaxLength(500);
        builder.Property(r => r.ProviderRunId).HasMaxLength(128);
        builder.Property(r => r.GrossTotal).HasMoneyPrecision();
        builder.Property(r => r.DeductionTotal).HasMoneyPrecision();
        builder.Property(r => r.EmployerCostTotal).HasMoneyPrecision();
        builder.Property(r => r.NetTotal).HasMoneyPrecision();

        if (multiTenancyEnabled)
        {
            // Number 过账时才分配，草稿为 null；唯一索引排除 null 与软删除行
            builder.HasIndex(r => new { r.TenantId, r.Number }).IsUnique()
                .HasFilter(IndexFilterFactory.GetColumnNotNullAndIsDeletedFalse("Number"));
            // ProviderRunId 外部摄取幂等键，内部批次为 null
            builder.HasIndex(r => new { r.TenantId, r.ProviderRunId }).IsUnique()
                .HasFilter(IndexFilterFactory.GetColumnNotNullAndIsDeletedFalse("ProviderRunId"));
            builder.HasIndex(r => new { r.TenantId, r.Status });
        }
        else
        {
            builder.HasIndex(r => r.Number).IsUnique()
                .HasFilter(IndexFilterFactory.GetColumnNotNullAndIsDeletedFalse("Number"));
            builder.HasIndex(r => r.ProviderRunId).IsUnique()
                .HasFilter(IndexFilterFactory.GetColumnNotNullAndIsDeletedFalse("ProviderRunId"));
            builder.HasIndex(r => r.Status);
        }
    }
}
