namespace Tnzi.Payment.Entities.Configs;

/// <summary>
/// 计费账户资料配置
/// </summary>
public class BillingAccountConfiguration : EntityTypeConfigurationBase<BillingAccount, Guid>
{
    public override void Configure(EntityTypeBuilder<BillingAccount> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(a => a.AccountType).HasMaxLength(BillingAccountRef.TypeMaxLength).IsRequired();
        builder.Property(a => a.AccountId).HasMaxLength(BillingAccountRef.IdMaxLength).IsRequired();
        builder.Property(a => a.DisplayName).HasMaxLength(256);
        builder.Property(a => a.BillingEmails).HasMaxLength(BillingAccount.BillingEmailsMaxLength);
        builder.Property(a => a.Address).HasMaxLength(1000);
        builder.Property(a => a.TaxId).HasMaxLength(64);

        // 每个账户至多一行资料（软删之后可以重建）
        if (multiTenancyEnabled)
        {
            builder.HasIndex(a => new { a.TenantId, a.AccountType, a.AccountId }, "IX_BillingAccount_Account").IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        }
        else
        {
            builder.HasIndex(a => new { a.AccountType, a.AccountId }, "IX_BillingAccount_Account").IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        }
    }
}
