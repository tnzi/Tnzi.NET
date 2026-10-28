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
            builder.HasIndex(e => new { e.TenantId, e.RecurringDocumentId, e.Status });
            builder.HasIndex(e => new { e.TenantId, e.Status, e.RecurringDocumentId });
        }
        else
        {
            builder.HasIndex(e => new { e.RecurringDocumentId, e.PeriodDate })
                .IsUnique()
                .HasFilter(IndexFilterFactory.GetColumnNotEquals("Status", (int)RecurringRunStatus.Failed));
            builder.HasIndex(e => new { e.RecurringDocumentId, e.Status });
            builder.HasIndex(e => new { e.Status, e.RecurringDocumentId });
        }

        // ★ 上面那条幂等键是**过滤**索引（Status <> Failed），用不上它的查询恰恰是最热的几条：
        // 每轮扫描找「还有失败期次的模板」（Status = Failed，与过滤条件矛盾）、逐模板捡失败期次
        // （RecurringDocumentId + Status = Failed）、历史抽屉按模板列记录（不蕴含过滤条件）。
        // 本表没有导航与外键，EF 不会自动补 RecurringDocumentId 的索引；不补的话这几条每次都扫全表，
        // 而表只增不减（每模板每期一行，外加失败行）。所以另立两条**不带过滤**的索引：
        // 按模板起头的覆盖逐模板查询与历史抽屉，按状态起头的覆盖扫描时的 Status = Failed 去重。

        builder.HasIndex(e => e.CreationTime);
    }
}
