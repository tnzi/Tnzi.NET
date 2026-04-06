namespace Tnzi.AI.Rag.Entities.Configs;

/// <summary>
/// KnowledgeGraphEdge 实体配置
/// </summary>
public class KnowledgeGraphEdgeConfiguration : EntityTypeConfigurationBase<KnowledgeGraphEdge, Guid>
{
    public override Type? DbContextType => typeof(RagDbContext);

    public override void Configure(EntityTypeBuilder<KnowledgeGraphEdge> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.RelationType)
            .HasMaxLength(200)
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
        builder.HasIndex(e => e.SourceNodeId);
        builder.HasIndex(e => e.TargetNodeId);
        builder.HasIndex(e => new { e.SourceNodeId, e.TargetNodeId });

        // 外键关系：KnowledgeBase → 级联删除（删除 KB 时清理图数据）
        builder.HasOne<KnowledgeBase>()
            .WithMany()
            .HasForeignKey(e => e.KnowledgeBaseId)
            .OnDelete(DeleteBehavior.Cascade);

        // 外键关系：Source/Target Node → 均级联删除（删除 node 时同步删除关联边）
        builder.HasOne<KnowledgeGraphNode>()
            .WithMany()
            .HasForeignKey(e => e.SourceNodeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<KnowledgeGraphNode>()
            .WithMany()
            .HasForeignKey(e => e.TargetNodeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
