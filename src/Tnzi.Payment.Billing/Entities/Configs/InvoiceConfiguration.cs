namespace Tnzi.Payment.Billing.Entities;

public class InvoiceConfiguration : EntityTypeConfigurationBase<Invoice, Guid>
{
    public override void Configure(EntityTypeBuilder<Invoice> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(i => i.InvoiceNo).HasMaxLength(64).IsRequired();
        builder.Property(i => i.Currency).HasMaxLength(8).IsRequired().HasDefaultValue("USD");
        builder.Property(i => i.Amount).HasMoneyPrecision();
        builder.Property(i => i.TaxAmount).HasMoneyPrecision();
        builder.Property(i => i.DiscountAmount).HasMoneyPrecision();
        builder.Property(i => i.DueAmount).HasMoneyPrecision();
        builder.Property(i => i.PaidAmount).HasMoneyPrecision();
        builder.Property(i => i.CustomerName).HasMaxLength(128);
        builder.Property(i => i.CustomerEmail).HasMaxLength(256);
        builder.Property(i => i.CustomerCompany).HasMaxLength(256);
        builder.Property(i => i.CustomerTaxId).HasMaxLength(64);
        builder.Property(i => i.CustomerAddress).HasMaxLength(500);
        builder.Property(i => i.BillingAddress).HasMaxLength(500);
        builder.Property(i => i.TemplateName).HasMaxLength(64);
        builder.Property(i => i.Notes).HasMaxLength(1000);
        builder.Property(i => i.InternalNotes).HasMaxLength(1000);
        builder.Property(i => i.PdfFileUrl).HasMaxLength(512);
        builder.Property(i => i.PdfFilePath).HasMaxLength(512);

        builder.HasMany(i => i.LineItems)
            .WithOne(l => l.Invoice)
            .HasForeignKey(l => l.InvoiceId)
            .HasPrincipalKey(i => i.Id);

        // 一对一「支付 → 发票」改由依赖端声明。拆分前它写在父模块的 PaymentConfiguration 里
        // （`HasOne(p => p.Invoice).WithOne(i => i.Payment)`），那是一条父 → 子的导航，
        // 父模块因此无法在不加载本包时编译。这里从 Invoice 一侧原样重建：
        // 外键列、主键列、约束名（EF 的默认名只取依赖表 / 主表 / 外键列，与导航属性名无关）
        // 与关系基数全部不变，因此**不产生迁移**。反向导航刻意不声明 —— 父模块不该认识发票。
        builder.HasOne(i => i.Payment)
            .WithOne()
            .HasForeignKey<Invoice>(i => i.PaymentId)
            .HasPrincipalKey<PaymentEntity>(p => p.Id);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(i => new { i.TenantId, i.InvoiceNo }).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
            builder.HasIndex(i => i.TenantId);
        }
        else
        {
            builder.HasIndex(i => i.InvoiceNo).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        }

        builder.HasIndex(i => i.Status);
        builder.HasIndex(i => i.InvoiceDate);
        builder.HasIndex(i => i.CustomerEmail);
        builder.HasIndex(i => i.UserId);
        // 一笔支付至多一张发票：幂等最终由数据库唯一约束兜底，
        // 应用层查重挡不住并发投递的同一个支付完成事件。
        builder.HasIndex(i => i.PaymentId).IsUnique()
            .HasFilter(IndexFilterFactory.GetColumnNotNullAndIsDeletedFalse(nameof(Invoice.PaymentId)));
    }
}
