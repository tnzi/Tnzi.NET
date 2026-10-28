namespace Tnzi.Payment.Entities.Configs;


public class RefundConfiguration : EntityTypeConfigurationBase<Refund, Guid>
{
    public override void Configure(EntityTypeBuilder<Refund> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(r => r.RefundNo).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Currency).HasMaxLength(8).IsRequired().HasDefaultValue("USD");
        builder.Property(r => r.RefundAmount).HasMoneyPrecision();
        builder.Property(r => r.Reason).HasMaxLength(500);
        builder.Property(r => r.ExternalRefundNo).HasMaxLength(128);
        builder.Property(r => r.ApproveRemark).HasMaxLength(500);
        builder.Property(r => r.BusinessOrderNo).HasMaxLength(128);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(r => new { r.TenantId, r.RefundNo }).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
            builder.HasIndex(r => r.TenantId);
        }
        else
        {
            builder.HasIndex(r => r.RefundNo).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        }

        // 渠道退款号全局唯一：渠道后台手工退的那一笔经回调记下时按它幂等（重投、并发两次投递都只记一行）。
        // 不分租户：它是渠道发的号，跨租户也不会重复。
        builder.HasIndex(r => r.ExternalRefundNo).IsUnique()
            .HasDatabaseName("IX_Refund_ExternalRefundNo")
            .HasFilter(IndexFilterFactory.GetCodeNotNullAndIsDeletedFalse(nameof(Refund.ExternalRefundNo), "IsDeleted"));

        builder.HasIndex(r => r.PaymentId);
        builder.HasIndex(r => r.Status);
        builder.HasIndex(r => r.CreationTime);
    }
}
