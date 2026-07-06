namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 会计凭证配置
/// </summary>
public class JournalEntryConfiguration : EntityTypeConfigurationBase<JournalEntry, Guid>
{
    public override void Configure(EntityTypeBuilder<JournalEntry> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Number).HasMaxLength(64);
        builder.Property(e => e.Memo).HasMaxLength(500);
        builder.Property(e => e.Currency).HasMaxLength(8).IsRequired();
        builder.Property(e => e.SourceType).HasMaxLength(64);
        builder.Property(e => e.SourceId).HasMaxLength(64);
        builder.Property(e => e.ExchangeRate).HasExchangeRatePrecision();
        builder.Property(e => e.TotalDebit).HasMoneyPrecision();
        builder.Property(e => e.TotalCredit).HasMoneyPrecision();

        builder.HasMany(e => e.Lines)
            .WithOne(l => l.JournalEntry)
            .HasForeignKey(l => l.JournalEntryId)
            .OnDelete(DeleteBehavior.Cascade);

        if (multiTenancyEnabled)
        {
            // Number 过账时才分配，草稿为 null；唯一索引排除 null 与软删除行
            builder.HasIndex(e => new { e.TenantId, e.Number }).IsUnique()
                .HasFilter(IndexFilterFactory.GetColumnNotNullAndIsDeletedFalse("Number"));
            builder.HasIndex(e => e.TenantId);
        }
        else
        {
            builder.HasIndex(e => e.Number).IsUnique()
                .HasFilter(IndexFilterFactory.GetColumnNotNullAndIsDeletedFalse("Number"));
        }

        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.PostingDate);
        builder.HasIndex(e => new { e.SourceType, e.SourceId });
    }
}
