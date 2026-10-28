namespace Tnzi.Finance.Banking.Entities.Configs;

/// <summary>
/// 银行规则条件配置
/// </summary>
public class BankRuleConditionConfiguration : EntityTypeConfigurationBase<BankRuleCondition, Guid>
{
    public override void Configure(EntityTypeBuilder<BankRuleCondition> builder)
    {
        builder.Property(e => e.Value).HasMaxLength(BankingFieldLimits.BankRuleConditionValueMaxLength).IsRequired();

        builder.HasIndex(e => e.BankRuleId);
    }
}
