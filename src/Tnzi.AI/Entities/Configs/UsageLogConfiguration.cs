namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// UsageLog 实体配置类
/// </summary>
public class UsageLogConfiguration : EntityTypeConfigurationBase<UsageLog, Guid>
{
    public override void Configure(EntityTypeBuilder<UsageLog> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Provider)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.Model)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.OperationType)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.ErrorMessage)
            .HasMaxLength(2000);

        builder.Property(e => e.IpAddress)
            .HasMaxLength(50);

        builder.Property(e => e.UserAgent)
            .HasMaxLength(500);

        // LLM 单请求成本常见 1e-6 ~ 1e-4 量级（CostCalculator 按 6 位小数计算），
        // 显式高精度避免退回全局 (19, 4) 约定时被截断为 0。
        builder.Property(e => e.EstimatedCostUsd)
            .HasPrecision(19, 8);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => e.TenantId);
        }

        builder.HasIndex(e => e.AgentId);
        builder.HasIndex(e => e.ThreadId);
        builder.HasIndex(e => e.CreationTime);
        builder.HasIndex(e => new { e.Provider, e.Model });
    }
}
