namespace Tnzi.Payment.Entities.Configs;

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
    }
}
