namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 费用支出配置
/// </summary>
public class ExpenseConfiguration : EntityTypeConfigurationBase<Expense, Guid>
{
    public override void Configure(EntityTypeBuilder<Expense> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Number).HasMaxLength(64);
        builder.Property(e => e.Memo).HasMaxLength(500);
        builder.Property(e => e.Currency).HasMaxLength(8).IsRequired();
        builder.Property(e => e.ExchangeRate).HasExchangeRatePrecision();
        builder.Property(e => e.SubTotal).HasMoneyPrecision();
        builder.Property(e => e.TaxTotal).HasMoneyPrecision();
        builder.Property(e => e.Total).HasMoneyPrecision();
        builder.Property(e => e.BaseTotal).HasMoneyPrecision();

        builder.HasMany(e => e.Lines)
            .WithOne()
            .HasForeignKey(l => l.ExpenseId)
            .OnDelete(DeleteBehavior.Cascade);

        // 供应商可选（VendorId 可空），删除受限——镜像 Bill.Vendor 关系。
        builder.HasOne(e => e.Vendor).WithMany().HasForeignKey(e => e.VendorId).OnDelete(DeleteBehavior.Restrict);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.Number }).IsUnique()
                .HasFilter(IndexFilterFactory.GetColumnNotNullAndIsDeletedFalse("Number"));
            builder.HasIndex(e => e.TenantId);
        }
        else
        {
            builder.HasIndex(e => e.Number).IsUnique()
                .HasFilter(IndexFilterFactory.GetColumnNotNullAndIsDeletedFalse("Number"));
        }

        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.DocDate);
        builder.HasIndex(e => e.VendorId);
        builder.HasIndex(e => e.PaidFromAccountId);
    }
}
