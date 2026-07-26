namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// Agent 实体配置类
/// </summary>
public class AgentConfiguration : EntityTypeConfigurationBase<Agent, Guid>
{
    public override void Configure(EntityTypeBuilder<Agent> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Description)
            .HasMaxLength(1000);

        builder.Property(e => e.Provider)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.Model)
            .HasMaxLength(100);

        builder.Property(e => e.Instructions)
            .HasMaxLength(32000);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => e.TenantId)
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        }

        builder.Property(e => e.ExecutionMode)
            .HasDefaultValue(AgentExecutionMode.Single);

        builder.HasIndex(e => e.Name)
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        builder.HasIndex(e => e.IsEnabled);

        // v2.1 能力标签字段（JSON 值转换）
        // 注意：ToolGroups/SkillSlugs/KnowledgeBaseIds 不再是 Agent 列——已迁移到 junction grant。
        builder.Property(e => e.Domains)
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, TnziJsonDefaults.Options),
                v => v == null ? null : JsonSerializer.Deserialize<List<string>>(v, TnziJsonDefaults.Options))
            .HasMaxLength(2000);

        builder.Property(e => e.Roles)
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, TnziJsonDefaults.Options),
                v => v == null ? null : JsonSerializer.Deserialize<List<string>>(v, TnziJsonDefaults.Options))
            .HasMaxLength(2000);

        builder.Property(e => e.QualityTier)
            .HasDefaultValue(3);

        builder.Property(e => e.LatencyTier)
            .HasDefaultValue(3);

        builder.Property(e => e.CostTier)
            .HasDefaultValue(3);

        // YAML 定义同步字段
        builder.Property(e => e.Source)
            .IsRequired()
            .HasMaxLength(50)
            .HasDefaultValue("database");

        builder.Property(e => e.DefinitionHash)
            .HasMaxLength(128);

        // 人格（Soul）内联内容 - 与 Instructions 同为 Agent 自有提示词文本，非外键。
        builder.Property(e => e.Persona)
            .HasMaxLength(32000);

        // Provider 关联 FK（过渡期，与遗留字符串 Provider 并存）
        builder.HasOne(e => e.ProviderEntity)
            .WithMany()
            .HasForeignKey(e => e.ProviderId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(e => e.ProviderId);
    }
}
