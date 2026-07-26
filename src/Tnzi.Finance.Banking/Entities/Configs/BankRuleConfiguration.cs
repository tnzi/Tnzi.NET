namespace Tnzi.Finance.Banking.Entities.Configs;

/// <summary>
/// 银行规则配置
/// </summary>
public class BankRuleConfiguration : EntityTypeConfigurationBase<BankRule, Guid>
{
    public override void Configure(EntityTypeBuilder<BankRule> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.PaymentMethod).HasMaxLength(32);

        builder.HasMany(e => e.Conditions)
            .WithOne()
            .HasForeignKey(c => c.BankRuleId)
            .OnDelete(DeleteBehavior.Cascade);

        if (multiTenancyEnabled)
            builder.HasIndex(e => e.TenantId);

        // 评估按 (启用, 优先级) 取序，索引跟着这个访问形态走。
        builder.HasIndex(e => new { e.IsEnabled, e.Priority });
        builder.HasIndex(e => e.AccountId);
    }
}
