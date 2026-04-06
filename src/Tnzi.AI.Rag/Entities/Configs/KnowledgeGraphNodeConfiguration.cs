namespace Tnzi.AI.Rag.Entities.Configs;

/// <summary>
/// KnowledgeGraphNode 实体配置
/// </summary>
public class KnowledgeGraphNodeConfiguration : EntityTypeConfigurationBase<KnowledgeGraphNode, Guid>
{
    public override Type? DbContextType => typeof(RagDbContext);

    public override void Configure(EntityTypeBuilder<KnowledgeGraphNode> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.EntityType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.Name)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(e => e.Description)
            .HasMaxLength(2000);

        builder.Property(e => e.Properties)
            .HasMaxLength(4000);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => e.TenantId);
        }

        builder.HasIndex(e => e.KnowledgeBaseId);
        builder.HasIndex(e => e.EntityType);
        builder.HasIndex(e => new { e.KnowledgeBaseId, e.Name });

        // 外键关系：KnowledgeBase → 级联删除（删除 KB 时清理图数据）
        builder.HasOne<KnowledgeBase>()
            .WithMany()
            .HasForeignKey(e => e.KnowledgeBaseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
