namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 银行对账勾选行配置
/// </summary>
public class ReconciliationLineConfiguration : EntityTypeConfigurationBase<ReconciliationLine, Guid>
{
    public override void Configure(EntityTypeBuilder<ReconciliationLine> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        // 一行总账至多被一张对账勾选（撤销勾选 = 硬删，无软删过滤条件）
        builder.HasIndex(e => e.JournalLineId).IsUnique();
        builder.HasIndex(e => e.ReconciliationId);

        if (multiTenancyEnabled)
            builder.HasIndex(e => e.TenantId);
    }
}
