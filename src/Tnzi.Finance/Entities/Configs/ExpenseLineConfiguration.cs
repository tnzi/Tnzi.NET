namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 费用支出行配置
/// </summary>
public class ExpenseLineConfiguration : EntityTypeConfigurationBase<ExpenseLine, Guid>
{
    public override void Configure(EntityTypeBuilder<ExpenseLine> builder)
    {
        builder.Property(l => l.Description).HasMaxLength(500);
        builder.Property(l => l.Amount).HasMoneyPrecision();

        builder.HasIndex(l => l.ExpenseId);
    }
}
