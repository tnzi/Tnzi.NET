namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 银行对账配置
/// </summary>
public class ReconciliationConfiguration : EntityTypeConfigurationBase<Reconciliation, Guid>
{
    public override void Configure(EntityTypeBuilder<Reconciliation> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Note).HasMaxLength(500);
        builder.Property(e => e.StatementEndingBalance).HasMoneyPrecision();

        // 同一科目同时只允许一张 Draft（服务层 check-then-act 的并发竞态由此索引兜底）
        var draftOnly = IndexFilterFactory.GetColumnEqualsAndIsDeletedFalse("Status", (int)ReconciliationStatus.Draft);
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.AccountId }).IsUnique().HasFilter(draftOnly);
            builder.HasIndex(e => e.TenantId);
        }
        else
        {
            builder.HasIndex(e => e.AccountId).IsUnique().HasFilter(draftOnly);
        }

        builder.HasIndex(e => new { e.AccountId, e.Status });
        builder.HasIndex(e => e.StatementDate);
    }
}
