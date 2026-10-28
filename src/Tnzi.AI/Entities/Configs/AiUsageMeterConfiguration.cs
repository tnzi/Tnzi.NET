namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// <see cref="AiUsageMeter"/> 的配置：每个用户一行，唯一索引兜底并发首插。
/// </summary>
public class AiUsageMeterConfiguration : EntityTypeConfigurationBase<AiUsageMeter, Guid>
{
    public override void Configure(EntityTypeBuilder<AiUsageMeter> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.UserId).IsRequired();
        builder.Property(e => e.SessionCostUsd).HasPrecision(18, 6);
        builder.Property(e => e.WeeklyCostUsd).HasPrecision(18, 6);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.UserId }).IsUnique();
            builder.HasIndex(e => e.TenantId);
        }
        else
        {
            builder.HasIndex(e => e.UserId).IsUnique();
        }

        // 管理端按最近使用排列计量行
        builder.HasIndex(e => e.LastUsedAt);
    }
}
