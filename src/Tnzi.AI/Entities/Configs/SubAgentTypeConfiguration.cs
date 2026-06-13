namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// SubAgentType 实体配置类
/// </summary>
public class SubAgentTypeConfiguration : EntityTypeConfigurationBase<SubAgentType, Guid>
{
    public override void Configure(EntityTypeBuilder<SubAgentType> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.Description)
            .IsRequired()
            .HasMaxLength(500);

        // JSON 值转换列（与 AgentConfiguration 的 Domains/Roles 同模式）
        builder.Property(e => e.ToolGroups)
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, TnziJsonDefaults.Options),
                v => v == null ? null : JsonSerializer.Deserialize<List<string>>(v, TnziJsonDefaults.Options))
            .HasMaxLength(2000);

        builder.Property(e => e.ExcludedToolGroups)
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, TnziJsonDefaults.Options),
                v => v == null ? null : JsonSerializer.Deserialize<List<string>>(v, TnziJsonDefaults.Options))
            .HasMaxLength(2000);

        builder.Property(e => e.Instructions)
            .HasMaxLength(4000);

        builder.Property(e => e.DefaultModel)
            .HasMaxLength(200);

        builder.Property(e => e.CapabilityTags)
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, TnziJsonDefaults.Options),
                v => v == null ? null : JsonSerializer.Deserialize<List<string>>(v, TnziJsonDefaults.Options))
            .HasMaxLength(1000);

        // 唯一索引：(TenantId, Name)
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.Name })
                .IsUnique();
        }
        else
        {
            builder.HasIndex(e => e.Name)
                .IsUnique();
        }

        builder.HasIndex(e => e.IsEnabled);
    }
}
