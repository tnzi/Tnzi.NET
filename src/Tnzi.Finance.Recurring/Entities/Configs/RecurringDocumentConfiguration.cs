namespace Tnzi.Finance.Recurring.Entities.Configs;

/// <summary>
/// 周期性单据模板配置
/// </summary>
public class RecurringDocumentConfiguration : EntityTypeConfigurationBase<RecurringDocument, Guid>
{
    public override void Configure(EntityTypeBuilder<RecurringDocument> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Currency).HasMaxLength(8);
        builder.Property(e => e.PaymentMethod).HasMaxLength(32);
        builder.Property(e => e.Memo).HasMaxLength(500);

        builder.HasMany(e => e.Lines)
            .WithOne()
            .HasForeignKey(l => l.RecurringDocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        if (multiTenancyEnabled)
            builder.HasIndex(e => e.TenantId);

        // 扫描谓词就是 (Status, NextRunDate)：到期扫描是这张表最热的查询。
        builder.HasIndex(e => new { e.Status, e.NextRunDate });
        builder.HasIndex(e => e.PartyId);
        builder.HasIndex(e => e.Kind);
    }
}
