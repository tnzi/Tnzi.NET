namespace Tnzi.Payment.Entities.Configs;

public class PaymentConfiguration : EntityTypeConfigurationBase<Payment, Guid>
{
    public override void Configure(EntityTypeBuilder<Payment> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(p => p.TradeNo).HasMaxLength(64).IsRequired();
        builder.Property(p => p.ExternalTradeNo).HasMaxLength(128);
        builder.Property(p => p.BusinessOrderNo).HasMaxLength(128).IsRequired();
        builder.Property(p => p.ChannelCode).HasMaxLength(32).IsRequired();
        builder.Property(p => p.Currency).HasMaxLength(8).IsRequired().HasDefaultValue("USD");
        builder.Property(p => p.Description).HasMaxLength(500);
        builder.Property(p => p.OriginalAmount).HasMoneyPrecision();
        builder.Property(p => p.PaidAmount).HasMoneyPrecision();
        builder.Property(p => p.DiscountAmount).HasMoneyPrecision();
        builder.Property(p => p.TaxAmount).HasMoneyPrecision();
        builder.Property(p => p.PayableAmount).HasMoneyPrecision();
        builder.Property(p => p.CustomerName).HasMaxLength(256);
        builder.Property(p => p.CustomerEmail).HasMaxLength(256);
        // ChannelResponse和ExtraData存储JSON数据，不指定类型以保持数据库兼容性
        // EF Core会根据数据库提供者自动选择合适的类型

        builder.HasMany(p => p.Refunds)
            .WithOne(r => r.Payment)
            .HasForeignKey(r => r.PaymentId)
            .HasPrincipalKey(p => p.Id);

        // 「支付 → 发票」的一对一关系移到依赖端声明（子模块 Tnzi.Payment.Billing 的
        // InvoiceConfiguration）。外键本来就在发票那一侧，这里删掉的只是一条父 → 子的导航；
        // 列、索引、约束名与关系基数都不变，因此**不产生迁移**。
        // 不加载那个子模块时，Payment_Invoice 表不存在，本表也不再引用它。

        if (multiTenancyEnabled)
        {
            builder.HasIndex(p => new { p.TenantId, p.TradeNo }).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
            builder.HasIndex(p => p.TenantId);
        }
        else
        {
            builder.HasIndex(p => p.TradeNo).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        }

        builder.HasIndex(p => p.BusinessOrderNo);
        builder.HasIndex(p => p.Status);
        builder.HasIndex(p => p.ChannelCode);
        builder.HasIndex(p => p.CreationTime);
        builder.HasIndex(p => p.UserId);
        // 过期支付清扫：按状态 + 过期时间过滤
        builder.HasIndex(p => new { p.Status, p.ExpireTime });
    }
}
