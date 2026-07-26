namespace Tnzi.Finance.Banking.Entities.Configs;

/// <summary>
/// EFT 批次配置
/// </summary>
public class EftBatchConfiguration : EntityTypeConfigurationBase<EftBatch, Guid>
{
    public override void Configure(EntityTypeBuilder<EftBatch> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Number).HasMaxLength(32);
        builder.Property(e => e.Currency).HasMaxLength(8).IsRequired();
        builder.Property(e => e.FileName).HasMaxLength(128);
        builder.Property(e => e.VoidReason).HasMaxLength(256);
        builder.Property(e => e.TotalAmount).HasMoneyPrecision();

        // 编号非空唯一（草稿不占号）
        var numberNotNull = IndexFilterFactory.GetColumnNotNullAndIsDeletedFalse("Number");
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.Number }).IsUnique().HasFilter(numberNotNull);
            builder.HasIndex(e => e.TenantId);
        }
        else
        {
            builder.HasIndex(e => e.Number).IsUnique().HasFilter(numberNotNull);
        }

        builder.HasIndex(e => new { e.BankAccountId, e.Status });
    }
}
