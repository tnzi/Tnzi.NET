namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 税码组件配置（随税码整体重建，级联删除；税率被引用时禁删）
/// </summary>
public class TaxCodeComponentConfiguration : EntityTypeConfigurationBase<TaxCodeComponent, Guid>
{
    public override void Configure(EntityTypeBuilder<TaxCodeComponent> builder)
    {
        builder.HasOne<TaxCode>()
            .WithMany(c => c.Components)
            .HasForeignKey(c => c.TaxCodeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.Rate)
            .WithMany()
            .HasForeignKey(c => c.TaxRateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.TaxCodeId, c.TaxRateId }).IsUnique();
        builder.HasIndex(c => c.TaxRateId);
    }
}
