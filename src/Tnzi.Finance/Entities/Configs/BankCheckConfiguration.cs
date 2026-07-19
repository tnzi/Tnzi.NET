namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 支票记录配置
/// </summary>
public class BankCheckConfiguration : EntityTypeConfigurationBase<BankCheck, Guid>
{
    public override void Configure(EntityTypeBuilder<BankCheck> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.PayeeName).HasMaxLength(256);
        builder.Property(e => e.Currency).HasMaxLength(8);
        builder.Property(e => e.VoidReason).HasMaxLength(256);
        builder.Property(e => e.Amount).HasMoneyPrecision();

        // 同一银行账户内支票号唯一（占号留痕；显式号撞号由唯一索引兜底翻译 409）
        var notDeleted = IndexFilterFactory.GetIsDeletedFalse();
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.BankAccountId, e.CheckNumber }).IsUnique().HasFilter(notDeleted);
            builder.HasIndex(e => e.TenantId);
        }
        else
        {
            builder.HasIndex(e => new { e.BankAccountId, e.CheckNumber }).IsUnique().HasFilter(notDeleted);
        }

        builder.HasIndex(e => new { e.BankAccountId, e.Status });
        builder.HasIndex(e => e.PaymentEntryId);
    }
}
