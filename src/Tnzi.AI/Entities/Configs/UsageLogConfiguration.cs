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
