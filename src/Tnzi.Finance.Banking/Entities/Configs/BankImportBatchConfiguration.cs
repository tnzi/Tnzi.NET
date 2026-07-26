namespace Tnzi.Finance.Banking.Entities.Configs;

/// <summary>
/// 银行流水导入批次配置
/// </summary>
public class BankImportBatchConfiguration : EntityTypeConfigurationBase<BankImportBatch, Guid>
{
    public override void Configure(EntityTypeBuilder<BankImportBatch> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.FileName).HasMaxLength(256);
        builder.Property(e => e.StatementEndBalance).HasMoneyPrecision();

        builder.HasIndex(e => new { e.AccountId, e.CreationTime });

        if (multiTenancyEnabled)
            builder.HasIndex(e => e.TenantId);
    }
}
