namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 银行账户档案配置
/// </summary>
public class BankAccountConfiguration : EntityTypeConfigurationBase<BankAccount, Guid>
{
    public override void Configure(EntityTypeBuilder<BankAccount> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Name).HasMaxLength(128).IsRequired();
        builder.Property(e => e.BankName).HasMaxLength(128);
        builder.Property(e => e.RoutingNumber).HasMaxLength(16);
        builder.Property(e => e.InstitutionNumber).HasMaxLength(8);
        builder.Property(e => e.TransitNumber).HasMaxLength(8);
        builder.Property(e => e.AccountNumberEncrypted).HasMaxLength(512);
        builder.Property(e => e.AccountNumberMasked).HasMaxLength(32);
        builder.Property(e => e.Currency).HasMaxLength(8);
        builder.Property(e => e.FeedProviderKey).HasMaxLength(64);
        builder.Property(e => e.ExternalAccountId).HasMaxLength(128);
        builder.Property(e => e.FeedCursor).HasMaxLength(256);
        builder.Property(e => e.EftOriginatorId).HasMaxLength(32);
        builder.Property(e => e.EftOriginatorName).HasMaxLength(64);
        builder.Property(e => e.OffsetXMm).HasMoneyPrecision();
        builder.Property(e => e.OffsetYMm).HasMoneyPrecision();

        // 每个资金科目至多一个银行档案（check-then-act 竞态由唯一过滤索引兜底）
        var notDeleted = IndexFilterFactory.GetIsDeletedFalse();
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.AccountId }).IsUnique().HasFilter(notDeleted);
            builder.HasIndex(e => e.TenantId);
        }
        else
        {
            builder.HasIndex(e => e.AccountId).IsUnique().HasFilter(notDeleted);
        }
    }
}
