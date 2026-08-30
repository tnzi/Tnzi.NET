namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 银行存款单行配置
/// </summary>
public class DepositLineConfiguration : EntityTypeConfigurationBase<DepositLine, Guid>
{
    public override void Configure(EntityTypeBuilder<DepositLine> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(l => l.Description).HasMaxLength(500);
        builder.Property(l => l.Reference).HasMaxLength(128);
        builder.Property(l => l.Amount).HasMoneyPrecision();

        // 「一张收款至多被一张存活的存款单收走」是数据库保证的，不是先查再写保证的：
        // 两个人同时把同一张支票放进各自的存款单，两次读都会读到「还没人收」。
        // 声明落在可空的 ClaimedPaymentEntryId 上，作废时置空即释放（行本身留着，
        // 于是作废单据仍答得出当初装的是哪几笔）；其它款项行该列恒为 null，被过滤条件排除。
        if (multiTenancyEnabled)
        {
            builder.HasIndex(l => new { l.TenantId, l.ClaimedPaymentEntryId }).IsUnique()
                .HasFilter(IndexFilterFactory.GetColumnNotNull(nameof(DepositLine.ClaimedPaymentEntryId)));
            builder.HasIndex(l => l.TenantId);
        }
        else
        {
            builder.HasIndex(l => l.ClaimedPaymentEntryId).IsUnique()
                .HasFilter(IndexFilterFactory.GetColumnNotNull(nameof(DepositLine.ClaimedPaymentEntryId)));
        }

        builder.HasIndex(l => l.DepositId);
        builder.HasIndex(l => l.PaymentEntryId);
    }
}
