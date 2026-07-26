namespace Tnzi.Finance.Recurring.Entities.Configs;

/// <summary>
/// 生成记录配置
/// </summary>
public class RecurringRunConfiguration : EntityTypeConfigurationBase<RecurringRun, Guid>
{
    public override void Configure(EntityTypeBuilder<RecurringRun> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.DocType).HasMaxLength(64);
        builder.Property(e => e.DocNumber).HasMaxLength(64);
        builder.Property(e => e.FailReason).HasMaxLength(1000);

        // ★幂等键：同一模板的同一期次至多成功一次。两个实例同时扫到、或作业被
        // 手工重跑，第二个的插入会撞这条索引而不是给客户重开一张发票。
        // 只覆盖非失败行 —— 失败的那一期必须能重试。
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.RecurringDocumentId, e.PeriodDate })
                .IsUnique()
                .HasFilter(IndexFilterFactory.GetColumnNotEquals("Status", (int)RecurringRunStatus.Failed));
            builder.HasIndex(e => e.TenantId);
        }
        else
        {
            builder.HasIndex(e => new { e.RecurringDocumentId, e.PeriodDate })
                .IsUnique()
                .HasFilter(IndexFilterFactory.GetColumnNotEquals("Status", (int)RecurringRunStatus.Failed));
        }

        builder.HasIndex(e => e.CreationTime);
    }
}
